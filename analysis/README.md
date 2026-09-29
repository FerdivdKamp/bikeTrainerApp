# ErgTrainer recording analysis

This folder is a separate Python environment for inspecting the raw BLE CSV
files written by ErgTrainer. It deliberately does not change the Windows Forms
application or its recording location.

## First-time setup (Windows PowerShell)

From the repository root, create a virtual environment and install the notebook
dependencies:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
python -m pip install -r .\analysis\requirements.txt
```

Start Jupyter Lab from the repository root so the notebook can use paths relative
to the repository:

```powershell
python -m jupyter lab
```

Open `analysis/raw_signal_analysis.ipynb`. If PowerShell does not permit
activation scripts, run the same commands with `.\.venv\Scripts\python.exe`
instead.

## Input recordings

ErgTrainer writes raw captures to:

```text
Documents\ErgTrainer\Recordings\raw-signals-YYYYMMDD-HHMMSSfff.csv
```

The notebook selects the newest file there by default. Set `RECORDING_FILE` in
its configuration cell to analyse a specific capture. Raw recordings and third
party exports are personal workout data, so they are intentionally not included
in this repository.

## What the notebook does

It keeps the raw packets alongside decoded values and uses the standard BLE
layouts for these recorded characteristics:

- Heart Rate Measurement (`0x2A37`): heart rate and optional RR intervals.
- FTMS Indoor Bike Data (`0x2AD2`): speed, cadence, power, and the optional
  fields that determine their byte offsets.
- Cycling Power Measurement (`0x2A63`): instantaneous power and crank-event
  data used to derive cadence between packets.

The final cells load CSV, FIT (Garmin/MyWhoosh), or GPX (Strava) exports into a
common timestamped table for visual comparison. Synchronize clocks or record a
shared start marker before treating a close visual match as proof of a byte
mapping.
