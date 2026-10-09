# ErgTrainer CSV reader

This small Python project contains a JupyterLab notebook for opening ErgTrainer raw-signal recordings.

## First-time setup (PowerShell)

From this directory:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
python -m pip install -e .
python -m jupyter lab
```

Open `notebooks/recording_browser.ipynb`. Its default recording folder is
`Documents/ErgTrainer/Recordings`, matching the Windows application. Change the path in the first code cell if recordings are stored elsewhere, then run the cells from top to bottom. After selecting another file, rerun from **Load and check** onward.

The notebook checks packet counts and timing, decodes standard heart rate and cycling power fields, compares HR polls with notifications, and shows a byte-level summary of trainer packets. It also reconstructs the cadence estimate produced by the current app parser and compares both recordings. That estimate is labelled separately from standard decoded fields because the older trainer packets do not announce crank revolution data. New recordings can include Cycling Speed and Cadence Measurement (2A5B) notifications when the trainer offers them; the notebook calculates cadence from their crank revolution counters and event times. HR packets may contain several RR intervals, and polls can repeat an earlier notification. The notebook only reads the CSV files.

To leave the virtual environment:

```powershell
deactivate
```

The `.venv` folder is local-only; it is intentionally not included in the project files.
