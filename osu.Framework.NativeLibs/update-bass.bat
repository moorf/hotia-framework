@echo off
setlocal enabledelayedexpansion

REM ensure we're running from the correct directory (location of this file).
cd /d "%~dp0"

REM create destination directories (tar, unlike unzip -d, does not create them)
for %%D in (
    "runtimes\win-x64\native"
    "runtimes\win-x86\native"
    "runtimes\win-arm64\native"
    "runtimes\linux-arm64\native"
    "runtimes\linux-x86\native"
    "runtimes\linux-x64\native"
    "runtimes\osx\native"
    "..\osu.Framework.iOS\runtimes\ios\native"
    "..\osu.Framework.Android\arm64-v8a"
    "..\osu.Framework.Android\armeabi-v7a"
    "..\osu.Framework.Android\x86"
) do (
    if not exist %%D mkdir %%D
)

REM bass
curl -fLso bass.zip https://www.un4seen.com/stuff/bass.zip || goto :download_error
tar -xf bass.zip -C runtimes\win-x64\native --strip-components=1 x64/bass.dll
tar -xf bass.zip -C runtimes\win-x86\native bass.dll
curl -fLso bass24-arm64.zip https://www.un4seen.com/stuff/bass-arm64.zip || goto :download_error
tar -xf bass24-arm64.zip -C runtimes\win-arm64\native bass.dll
curl -fLso bass-linux.zip https://www.un4seen.com/stuff/bass-linux.zip || goto :download_error
tar -xf bass-linux.zip -C runtimes\linux-arm64\native --strip-components=1 aarch64/libbass.so
tar -xf bass-linux.zip -C runtimes\linux-x86\native --strip-components=1 x86/libbass.so
tar -xf bass-linux.zip -C runtimes\linux-x64\native --strip-components=1 x86_64/libbass.so
curl -fLso bass-osx.zip https://www.un4seen.com/stuff/bass-osx.zip || goto :download_error
tar -xf bass-osx.zip -C runtimes\osx\native libbass.dylib
curl -fLso bass24-ios.zip https://www.un4seen.com/stuff/bass-ios.zip || goto :download_error
tar -xf bass24-ios.zip -C ..\osu.Framework.iOS\runtimes\ios\native bass.xcframework
curl -fLso bass24-android.zip https://www.un4seen.com/stuff/bass-android.zip || goto :download_error
tar -xf bass24-android.zip -C ..\osu.Framework.Android\arm64-v8a --strip-components=1 arm64-v8a
tar -xf bass24-android.zip -C ..\osu.Framework.Android\armeabi-v7a --strip-components=1 armeabi-v7a
tar -xf bass24-android.zip -C ..\osu.Framework.Android\x86 --strip-components=1 x86

REM bassfx
curl -fLso bass_fx.zip https://www.un4seen.com/stuff/bass_fx.zip || goto :download_error
tar -xf bass_fx.zip -C runtimes\win-x64\native --strip-components=1 x64/bass_fx.dll
tar -xf bass_fx.zip -C runtimes\win-x86\native bass_fx.dll
REM best-effort: not every bass-arm64.zip release bundles bass_fx.dll, so don't treat this as fatal
tar -xf bass24-arm64.zip -C runtimes\win-arm64\native bass_fx.dll 2>nul
curl -fLso bass_fx-linux.zip https://www.un4seen.com/stuff/bass_fx-linux.zip || goto :download_error
tar -xf bass_fx-linux.zip -C runtimes\linux-arm64\native --strip-components=1 aarch64/libbass_fx.so
tar -xf bass_fx-linux.zip -C runtimes\linux-x86\native --strip-components=1 x86/libbass_fx.so
tar -xf bass_fx-linux.zip -C runtimes\linux-x64\native --strip-components=1 x86_64/libbass_fx.so
curl -fLso bass_fx-osx.zip https://www.un4seen.com/stuff/bass_fx-osx.zip || goto :download_error
tar -xf bass_fx-osx.zip -C runtimes\osx\native libbass_fx.dylib
curl -fLso bass_fx24-ios.zip https://www.un4seen.com/files/z/0/bass_fx24-ios.zip || goto :download_error
tar -xf bass_fx24-ios.zip -C ..\osu.Framework.iOS\runtimes\ios\native bass_fx.xcframework
curl -fLso bass_fx24-android.zip https://www.un4seen.com/files/z/0/bass_fx24-android.zip || goto :download_error
tar -xf bass_fx24-android.zip -C ..\osu.Framework.Android\arm64-v8a --strip-components=2 libs/arm64-v8a
tar -xf bass_fx24-android.zip -C ..\osu.Framework.Android\armeabi-v7a --strip-components=2 libs/armeabi-v7a
tar -xf bass_fx24-android.zip -C ..\osu.Framework.Android\x86 --strip-components=2 libs/x86

REM bassmix
curl -fLso bassmix24.zip https://www.un4seen.com/stuff/bassmix.zip || goto :download_error
tar -xf bassmix24.zip -C runtimes\win-x64\native --strip-components=1 x64/bassmix.dll
tar -xf bassmix24.zip -C runtimes\win-x86\native bassmix.dll
REM best-effort: not every bass-arm64.zip release bundles bassmix.dll, so don't treat this as fatal
tar -xf bass24-arm64.zip -C runtimes\win-arm64\native bassmix.dll 2>nul
curl -fLso bassmix24-linux.zip https://www.un4seen.com/stuff/bassmix-linux.zip || goto :download_error
tar -xf bassmix24-linux.zip -C runtimes\linux-arm64\native --strip-components=1 aarch64/libbassmix.so
tar -xf bassmix24-linux.zip -C runtimes\linux-x86\native --strip-components=1 x86/libbassmix.so
tar -xf bassmix24-linux.zip -C runtimes\linux-x64\native --strip-components=1 x86_64/libbassmix.so
curl -fLso bassmix24-osx.zip https://www.un4seen.com/stuff/bassmix-osx.zip || goto :download_error
tar -xf bassmix24-osx.zip -C runtimes\osx\native libbassmix.dylib
curl -fLso bassmix24-ios.zip https://www.un4seen.com/files/bassmix24-ios.zip || goto :download_error
tar -xf bassmix24-ios.zip -C ..\osu.Framework.iOS\runtimes\ios\native bassmix.xcframework
curl -fLso bassmix24-android.zip https://www.un4seen.com/files/bassmix24-android.zip || goto :download_error
tar -xf bassmix24-android.zip -C ..\osu.Framework.Android\arm64-v8a --strip-components=2 libs/arm64-v8a
tar -xf bassmix24-android.zip -C ..\osu.Framework.Android\armeabi-v7a --strip-components=2 libs/armeabi-v7a
tar -xf bassmix24-android.zip -C ..\osu.Framework.Android\x86 --strip-components=2 libs/x86

curl -fLso bassasio14.zip https://www.un4seen.com/files/bassasio14.zip || goto :download_error
tar -xf bassasio14.zip -C runtimes\win-x64\native --strip-components=1 x64/bassasio.dll
tar -xf bassasio14.zip -C runtimes\win-x86\native bassasio.dll

goto :cleanup

:download_error
echo.
echo ERROR: a download failed (curl -f treats HTTP errors as failures).
echo Check your network connection / that the un4seen.com URL above is reachable,
echo then re-run this script.
exit /b 1

:cleanup
REM clean up
del /q bass*.zip

endlocal