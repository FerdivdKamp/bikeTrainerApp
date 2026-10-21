# Prompt: Garmin Connect compatible session recording

Implement a recorded indoor-cycling activity that the user can import into Garmin Connect. Work in the phases below; keep each phase small and reviewable. Read `agents.md` and inspect `MainForm.cs`, `Sessions/`, `Sensors/`, and `Recorders/` before changing code. Preserve the .NET 8 WinForms target, x64 platform, existing BLE behavior, raw-signal CSVs, and session event CSVs. Do not alter or delete existing user recordings. Do not commit or push.

The current `SessionEventLog` records starts, block changes, pauses, reconnects, and completion, but no continuous telemetry. `SignalRecorder` stores diagnostic BLE payloads only when enabled. `DataRecorder` is not wired into `MainForm`. Neither current CSV is a Garmin Connect activity file. Do not present an event-only log as a completed ride or invent missing historical telemetry.

## Phase 1 — Record an actual workout

Add a separate, local activity recorder for running training sessions. Capture UTC start/end times and timestamped power, cadence, speed, and heart-rate values from the existing processed telemetry paths. Represent missing values as missing, not zero. Record pause/resume and disconnect intervals so elapsed time and active timer time remain distinct. Capture the session name, indoor-cycling sport, and block boundaries where available. Do not invent GPS coordinates, distance, calories, or training metrics; include distance only if it can be derived transparently from valid speed data. Keep telemetry capture bounded and responsive, and make writes safe across UI and device callbacks. Flush and close on completion, Stop, form close, and recoverable failures. Store activity data under `Documents/ErgTrainer/Activities` with unique filenames; expose the final path in the UI. Keep this recording independent of the optional raw BLE checkbox. Explain what happens if the app exits unexpectedly.

**Done when:** A completed, paused, stopped, and disconnected test session each leaves a readable activity record with correct timestamps and sample gaps. Existing session event and raw signal logs still work.

## Phase 2 — Export a Garmin Connect activity file

Export each activity as a FIT **Activity** file, using Garmin's official FIT SDK for C# if its current license and package fit the project. Check framework and existing dependencies before adding a package. Include required FIT file, session, and activity messages; add record and event/lap messages where appropriate. Use indoor cycling, valid UTC timestamps, measured samples, and correct elapsed versus timer durations. Do not confuse a FIT Activity with a FIT Workout (the latter describes planned steps). Keep a stable link between the local activity and its export, and provide an Export/Open folder action. Do not overwrite an existing export silently.

Validate structure with official FIT SDK decoding/integrity tools and deterministic hardware-independent sample data. Then manually import a real exported file into Garmin Connect Web and check that the activity appears with the expected date, duration, power/cadence/heart-rate plots where data exists, and no fabricated GPS track. Record any fields Garmin Connect drops or changes. If FIT export is blocked, assess TCX as a documented fallback and state its limits.

**Done when:** A sample export passes FIT validation and a real session imports successfully through Garmin Connect Web. The original local activity remains usable if import fails.

## Phase 3 — Connect to Garmin Connect through account sign-in

Add an explicit **Connect to Garmin Connect** flow. First verify Garmin's *current, officially supported* authentication and activity-upload options for third-party desktop applications, including whether this application can obtain the necessary developer approval. Use the user's default browser for Garmin's own sign-in page. Let the user use their Garmin login, or **Continue with Google only if Garmin itself offers that option** on its sign-in page. Never collect Garmin or Google passwords in the WinForms app, embed a fake sign-in form, automate website login, or store browser cookies.

If Garmin provides and approves an official API that accepts activity uploads for this app, implement its documented authorization flow with least-privilege scopes, secure token storage, disconnect/revoke behavior, clear upload status, retry without duplicate activities, and explicit user consent before each first upload. Validate with a test account and handle expired authorization and rejected uploads. If no supported upload API is available, make the connection phase a browser handoff: open Garmin Connect's sign-in/import page and the exported file's folder, then guide the user through **Upload/Import Data**. Label this as manual import, not automatic account synchronization. Google sign-in is governed by the options Garmin presents; do not implement a separate Google OAuth flow and assume it grants Garmin access.

**Done when:** The user can sign in safely through Garmin's site and get the exported activity into their account through a supported path. The UI accurately distinguishes a connected integration from browser-based manual import.

## Phase 4 — Finish the user flow and documentation

Show recording and export state clearly during and after a workout. Provide a concise import guide, including where local activity, FIT export, raw BLE, and event CSV files live. Explain that old event-only CSVs cannot reconstruct a full activity; offer any historical conversion only for data actually present. Check duplicate imports, no-HRM sessions, short sessions, pause/reconnect timing, partial file recovery, and app shutdown. Run `dotnet build .\ergtrainer.sln`; report build and FIT validation separately from BLE hardware and Garmin account verification.

## Official references to recheck when implementing

- [Garmin Connect manual activity import](https://support.garmin.com/en-US/?faq=Ht3ZP52Kju075uKvqTqu99) — activity files are FIT, GPX, or TCX; ordinary ErgTrainer CSV is not a supported activity import.
- [Garmin FIT Activity file requirements](https://developer.garmin.com/fit/file-types/activity)
- [Garmin FIT SDK for C#](https://developer.garmin.com/fit/get-the-sdk/)
- [Garmin Connect Activity API](https://developer.garmin.com/gc-developer-program/activity-api/) — describes access to activities synced *from* Garmin Connect; do not assume it provides third-party upload permission.
