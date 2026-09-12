// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
//
// Copyright (c) moorf. Modified 2026.
// Modifications released under the GNU General Public License v3.0.
// See the LICENCE.GPL3 file in the repository root for full licence text.

using osu.Framework.Statistics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ManagedBass;
using ManagedBass.Asio;
using ManagedBass.Mix;
using ManagedBass.Wasapi;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Logging;
using osu.Framework.Platform.Linux.Native;

namespace osu.Framework.Threading
{
    public class AudioThread : GameThread
    {
        public AudioThread()
            : base(name: "Audio")
        {
            OnNewFrame += onNewFrame;
            PreloadBass();
        }

        public override bool IsCurrent => ThreadSafety.IsAudioThread;

        internal sealed override void MakeCurrent()
        {
            base.MakeCurrent();

            ThreadSafety.IsAudioThread = true;
        }

        internal override IEnumerable<StatisticsCounterType> StatisticsCounters => new[]
        {
            StatisticsCounterType.TasksRun,
            StatisticsCounterType.Tracks,
            StatisticsCounterType.Samples,
            StatisticsCounterType.SChannels,
            StatisticsCounterType.Components,
            StatisticsCounterType.MixChannels,
        };

        private readonly List<AudioManager> managers = new List<AudioManager>();

        /// <summary>
        /// Regular (non-ASIO) BASS device indices that are currently initialised via <see cref="Bass.Init(int, int, DeviceInitFlags, IntPtr, IntPtr)"/>.
        /// Device 0 (the "No sound" device) is kept initialised at all times as a safety net - see <see cref="ensureBassNoSoundDevice"/>.
        /// </summary>
        private static readonly HashSet<int> initialised_bass_devices = new HashSet<int>();

        private static readonly GlobalStatistic<double> cpu_usage = GlobalStatistics.Get<double>("Audio", "Bass CPU%");
        private static readonly GlobalStatistic<double> asio_cpu_usage = GlobalStatistics.Get<double>("Audio", "BassASIO CPU%");

        private long frameCount;

        private void onNewFrame()
        {
            if (frameCount++ % 1000 == 0)
            {
                cpu_usage.Value = Bass.CPUUsage;

                if (asioActive)
                    asio_cpu_usage.Value = BassAsio.CPUUsage;
            }

            lock (managers)
            {
                for (int i = 0; i < managers.Count; i++)
                {
                    var m = managers[i];
                    m.Update();
                }
            }
        }

        internal void RegisterManager(AudioManager manager)
        {
            lock (managers)
            {
                if (managers.Contains(manager))
                    throw new InvalidOperationException($"{manager} was already registered");

                managers.Add(manager);
            }

            manager.GlobalMixerHandle.BindTo(globalMixerHandle);
        }

        internal void UnregisterManager(AudioManager manager)
        {
            lock (managers)
                managers.Remove(manager);

            manager.GlobalMixerHandle.UnbindFrom(globalMixerHandle);
        }

        protected override void OnExit()
        {
            base.OnExit();

            lock (managers)
            {
                // AudioManagers are iterated over backwards since disposal will unregister and remove them from the list.
                for (int i = managers.Count - 1; i >= 0; i--)
                {
                    var m = managers[i];

                    m.Dispose();

                    // Audio component disposal (including the AudioManager itself) is scheduled and only runs when the AudioThread updates.
                    // But the AudioThread won't run another update since it's exiting, so an update must be performed manually in order to finish the disposal.
                    m.Update();
                }

                managers.Clear();
            }

            // Safety net to ensure we have freed all devices before exiting.
            // This is mainly required for device-lost scenarios.
            // See https://github.com/ppy/osu-framework/pull/3378 for further discussion.
            foreach (int d in initialised_bass_devices.ToArray())
                FreeDevice(d, false);

            freeAsio();
            freeWasapi();
            freeBassNoSoundDevice();
        }

        #region BASS / BASSASIO Initialisation

        // TODO: All this bass init stuff should probably not be in this class.

        private WasapiProcedure? wasapiProcedure;
        private WasapiNotifyProcedure? wasapiNotifyProcedure;

        private AsioProcedure? asioProcedure;
        private int? activeAsioDevice;
        private bool asioActive => activeAsioDevice != null;

        private const int asio_mixer_frequency = 44100;

        /// <summary>
        /// If a global mixer is being used, this will be the BASS handle for it.
        /// If non-null, all game mixers should be added to this mixer.
        /// </summary>
        private readonly Bindable<int?> globalMixerHandle = new Bindable<int?>();

        /// <summary>
        /// Initialises an audio device, either a regular BASS output device or a BASSASIO device.
        /// </summary>
        /// <param name="deviceId">
        /// If <paramref name="isAsio"/> is <c>false</c>, this is a regular BASS device index.
        /// If <paramref name="isAsio"/> is <c>true</c>, this is a BASSASIO device index.
        /// </param>
        /// <param name="isAsio">Whether <paramref name="deviceId"/> refers to a BASSASIO device (e.g. ASIO4ALL, JackRouter) rather than a regular BASS device.</param>
        /// <param name="useExperimentalWasapi">Whether experimental WASAPI initialisation should be attempted. Only applies when <paramref name="isAsio"/> is <c>false</c>.</param>
        internal bool InitDevice(int deviceId, bool isAsio, bool useExperimentalWasapi)
        {
            Debug.Assert(ThreadSafety.IsAudioThread);

            // Ensure a "no sound" BASS context always exists, independent of which backend/device ends up being used.
            // This keeps decode-only stream creation (used internally by track/sample stores) working at all times.
            if (!ensureBassNoSoundDevice())
                return false;

            return isAsio ? initAsioDevice(deviceId) : initBassDevice(deviceId, useExperimentalWasapi);
        }

        /// <summary>
        /// Frees a previously initialised device.
        /// </summary>
        /// <param name="deviceId">The device index to free (interpreted the same way as in <see cref="InitDevice"/>).</param>
        /// <param name="isAsio">Whether <paramref name="deviceId"/> refers to a BASSASIO device.</param>
        internal void FreeDevice(int deviceId, bool isAsio)
        {
            Debug.Assert(ThreadSafety.IsAudioThread);

            if (isAsio)
            {
                freeAsio();
                return;
            }

            int selectedDevice = Bass.CurrentDevice;

            if (canSelectBassDevice(deviceId))
            {
                Bass.CurrentDevice = deviceId;
                Bass.Free();
            }

            freeWasapi();

            if (selectedDevice != deviceId && canSelectBassDevice(selectedDevice))
                Bass.CurrentDevice = selectedDevice;

            initialised_bass_devices.Remove(deviceId);

            static bool canSelectBassDevice(int deviceId) => Bass.GetDeviceInfo(deviceId, out var deviceInfo) && deviceInfo.IsInitialized;
        }

        /// <summary>
        /// Makes BASS available to be consumed.
        /// </summary>
        internal static void PreloadBass()
        {
            if (RuntimeInfo.OS == RuntimeInfo.Platform.Linux)
            {
                // required for the time being to address libbass_fx.so load failures (see https://github.com/ppy/osu/issues/2852)
                Library.Load("libbass.so", Library.LoadFlags.RTLD_LAZY | Library.LoadFlags.RTLD_GLOBAL);
            }
        }

        private bool initBassDevice(int deviceId, bool useExperimentalWasapi)
        {
            Trace.Assert(deviceId != -1); // The real device ID should always be used, as the -1 device has special cases which are hard to work with.

            // Only one output backend can be driving audio at a time - tear down ASIO if it was previously active.
            freeAsio();

            // Try to initialise the device, or request a re-initialise.
            if (!Bass.Init(deviceId, Flags: (DeviceInitFlags)128)) // 128 == BASS_DEVICE_REINIT
                return false;

            if (useExperimentalWasapi)
            {
                attemptWasapiInitialisation();
            }
            else
            {
                freeWasapi();
            }

            initialised_bass_devices.Add(deviceId);
            return true;
        }

        private void attemptWasapiInitialisation()
        {
            if (RuntimeInfo.OS != RuntimeInfo.Platform.Windows)
                return;

            int wasapiDevice = -1;

            // WASAPI device indices don't match normal BASS devices.
            // Each device is listed multiple times with each supported channel/frequency pair.
            //
            // Working backwards to find the correct device is how bass does things internally (see BassWasapi.GetBassDevice).
            if (Bass.CurrentDevice > 0)
            {
                string driver = Bass.GetDeviceInfo(Bass.CurrentDevice).Driver;

                if (!string.IsNullOrEmpty(driver))
                {
                    // In the normal execution case, BassWasapi.GetDeviceInfo will return false as soon as we reach the end of devices.
                    // This while condition is just a safety to avoid looping forever.
                    // It's intentionally quite high because if a user has many audio devices, this list can get long.
                    //
                    // Retrieving device info here isn't free. In the future we may want to investigate a better method.
                    while (wasapiDevice < 16384)
                    {
                        if (!BassWasapi.GetDeviceInfo(++wasapiDevice, out WasapiDeviceInfo info))
                            break;

                        if (info.ID == driver)
                            break;
                    }
                }
            }

            // To keep things in a sane state let's only keep one device initialised via wasapi.
            freeWasapi();
            initWasapi(wasapiDevice);
        }

        private void initWasapi(int wasapiDevice)
        {
            // This is intentionally initialised inline and stored to a field.
            // If we don't do this, it gets GC'd away.
            wasapiProcedure = (buffer, length, _) =>
            {
                if (globalMixerHandle.Value == null)
                    return 0;

                return Bass.ChannelGetData(globalMixerHandle.Value!.Value, buffer, length);
            };
            wasapiNotifyProcedure = (notify, device, _) => Scheduler.Add(() =>
            {
                if (notify == WasapiNotificationType.DefaultOutput)
                {
                    freeWasapi();
                    initWasapi(device);
                }
            });

            bool initialised = BassWasapi.Init(wasapiDevice, Procedure: wasapiProcedure, Flags: WasapiInitFlags.EventDriven | WasapiInitFlags.AutoFormat, Buffer: 0f, Period: float.Epsilon);

            if (!initialised)
                return;

            BassWasapi.GetInfo(out var wasapiInfo);
            globalMixerHandle.Value = BassMix.CreateMixerStream(wasapiInfo.Frequency, wasapiInfo.Channels, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
            BassWasapi.Start();

            BassWasapi.SetNotify(wasapiNotifyProcedure);
        }

        private void freeWasapi()
        {
            if (globalMixerHandle.Value == null) return;

            // the mixer handle may instead belong to an active ASIO session - don't tear that down from here.
            if (asioActive) return;

            // The mixer probably doesn't need to be recycled. Just keeping things sane for now.
            Bass.StreamFree(globalMixerHandle.Value.Value);
            BassWasapi.Stop();
            BassWasapi.Free();
            globalMixerHandle.Value = null;
        }

        private bool initAsioDevice(int asioDevice)
        {
            // Only one ASIO output can be active at a time.
            freeAsio();

            // Similarly, don't leave a regular output device running underneath the ASIO device.
            freeAllBassOutputDevices();

            Logger.Log($"Attempting BassASIO initialisation for device {asioDevice}");

            bool initialised = BassAsio.Init(asioDevice, AsioInitFlags.Thread);

            if (!initialised)
            {
                Logger.Log($"BassASIO failed to initialise device {asioDevice}: {BassAsio.LastError}", level: LogLevel.Error);
                return false;
            }

            activeAsioDevice = asioDevice;

            if (!BassAsio.GetInfo(out AsioInfo asioInfo))
            {
                Logger.Log("BassASIO initialised, but failed to retrieve device info.");
                freeAsio();
                return false;
            }

            int outputChannels = Math.Min(2, asioInfo.Outputs);

            if (outputChannels <= 0)
            {
                Logger.Log("BassASIO device has no output channels.");
                freeAsio();
                return false;
            }

            int mixer = BassMix.CreateMixerStream(
                asio_mixer_frequency,
                outputChannels,
                BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);

            if (mixer == 0)
            {
                Logger.Log("Failed to create BassASIO mixer.");
                freeAsio();
                return false;
            }

            globalMixerHandle.Value = mixer;

            // This is intentionally initialised inline and stored to a field.
            // If we don't do this, it gets GC'd away.
            asioProcedure = (input, channel, buffer, length, user) =>
            {
                if (input)
                    return 0;

                int? mixerHandle = globalMixerHandle.Value;

                if (mixerHandle == null)
                    return 0;

                int read = Bass.ChannelGetData(mixerHandle.Value, buffer, length);

                return Math.Max(0, read);
            };

            if (!BassAsio.ChannelEnable(false, 0, asioProcedure, IntPtr.Zero))
            {
                Logger.Log("Failed to enable BassASIO output channel 0.");
                freeAsio();
                return false;
            }

            for (int i = 1; i < outputChannels; i++)
            {
                if (!BassAsio.ChannelJoin(false, i, 0))
                {
                    Logger.Log($"Failed to join BassASIO output channel {i}.");
                    freeAsio();
                    return false;
                }
            }

            BassAsio.ChannelSetFormat(false, 0, AsioSampleFormat.Float);
            BassAsio.ChannelSetRate(false, 0, asio_mixer_frequency);

            if (!BassAsio.Start(0))
            {
                Logger.Log("Failed to start BassASIO.");
                freeAsio();
                return false;
            }

            Logger.Log($@"🔈 BASSASIO initialised
                          BASS MIX version:       {BassMix.Version}
                          ASIO device:             {asioDevice}
                          ASIO output channels:    {outputChannels}");

            return true;
        }

        private void freeAsio()
        {
            if (activeAsioDevice == null)
                return;

            int? mixer = globalMixerHandle.Value;

            // Ensure callbacks stop seeing the mixer before it is freed.
            globalMixerHandle.Value = null;

            BassAsio.Stop();

            if (mixer != null)
                Bass.StreamFree(mixer.Value);

            BassAsio.Free();

            activeAsioDevice = null;
            asioProcedure = null;
        }

        /// <summary>
        /// Frees every currently initialised regular BASS output device (but keeps the "No sound" device alive).
        /// Used when switching over to an ASIO device, since only one output backend should be driving audio at a time.
        /// </summary>
        private static void freeAllBassOutputDevices()
        {
            foreach (int d in initialised_bass_devices.ToArray())
            {
                if (d == Bass.NoSoundDevice)
                    continue;

                int selectedDevice = Bass.CurrentDevice;

                if (Bass.GetDeviceInfo(d, out var info) && info.IsInitialized)
                {
                    Bass.CurrentDevice = d;
                    Bass.Free();
                }

                if (selectedDevice != d && Bass.GetDeviceInfo(selectedDevice, out var selectedInfo) && selectedInfo.IsInitialized)
                    Bass.CurrentDevice = selectedDevice;

                initialised_bass_devices.Remove(d);
            }
        }

        private static bool ensureBassNoSoundDevice()
        {
            int selectedDevice = Bass.CurrentDevice;

            if (!isBassDeviceInitialised(Bass.NoSoundDevice))
            {
                if (!Bass.Init(Bass.NoSoundDevice))
                    return false;

                initialised_bass_devices.Add(Bass.NoSoundDevice);
            }

            if (selectedDevice != Bass.NoSoundDevice && isBassDeviceInitialised(selectedDevice))
                Bass.CurrentDevice = selectedDevice;

            return true;
        }

        private static void freeBassNoSoundDevice()
        {
            int selectedDevice = Bass.CurrentDevice;

            if (isBassDeviceInitialised(Bass.NoSoundDevice))
            {
                Bass.CurrentDevice = Bass.NoSoundDevice;
                Bass.Free();
            }

            initialised_bass_devices.Remove(Bass.NoSoundDevice);

            if (selectedDevice != Bass.NoSoundDevice && isBassDeviceInitialised(selectedDevice))
                Bass.CurrentDevice = selectedDevice;
        }

        private static bool isBassDeviceInitialised(int deviceId)
            => Bass.GetDeviceInfo(deviceId, out var deviceInfo) && deviceInfo.IsInitialized;

        #endregion
    }
}
