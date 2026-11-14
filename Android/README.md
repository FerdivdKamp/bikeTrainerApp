# Android MVP

This is an Android-only .NET application that reuses the existing sensor code.
It scans for devices, allows you to choose a trainer and heart-rate monitor,
shows live readings, and plays a four-beep sound test without requesting audio
focus. Keep the screen open during a ride; background workouts are not supported.

Install the .NET 10 Android workload, Android SDK and Java dependencies, then
build with `dotnet build .\Android\ErgTrainer.Android.csproj`. For a standalone
installable APK, publish a signed release build. Connect the phone for debugging
or transfer the signed APK to it.

On the phone, grant Nearby devices permission, scan and select each device.
Start Spotify, return to ErgTrainer, and tap **Test countdown sound** to check
the four beeps. The app uses the media volume and does not request audio focus;
the result still needs to be checked with your audio device and Spotify.

This MVP does not run workouts or control resistance. BLE behavior and the
sound mix must be verified on the physical phone, trainer and heart-rate monitor.
