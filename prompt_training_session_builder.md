# Prompt: Training Session Builder for ErgTrainer

Implement a structured indoor-cycling training session builder and runner with **ERG and Simulation sessions, coaching instructions, and animated workout progress**.

Follow `AGENTS.md` before editing.

Preserve:
- .NET 8 WinForms target and x86 platform.
- Existing BLE telemetry and Tacx FE-C over BLE functionality.
- Existing ERG control through `BleTacxTrainer.SetTargetPowerAsync`.
- FTMS test functionality and ANT+ heart-rate monitoring.
- Existing recordings, free-ride functionality, and local files.

Do not commit or push.

Inspect the existing implementation before making architectural decisions. Prefer extending existing components over duplicating their functionality.

## 1. Application structure

Add a `TabControl` to `MainForm` with two tabs:

1. **Trainer:** Existing live trainer interface.
2. **Session Builder:** Create, edit, load, save, and run workouts.

Keep device connections and telemetry shared across tabs. Switching tabs must not disconnect devices or interrupt an active session.

Make the layout work at the application's current window size. Use scrollable panels or appropriate layout containers where necessary.

Separate responsibilities:

- Session models and validation.
- Workout serialization and import/export.
- Session execution and timing.
- Trainer control.
- Coaching instructions.
- Progress visualization.

Keep session models, file conversion, and execution timing independent of WinForms and hardware APIs.

## 2. Session types

Every session has exactly one control mode:

- **ERG**
- **Simulation**

The mode is selected when creating a session and applies to every block, including warm-up, intervals, recovery, and cooldown.

**Do not support switching modes within a running session.**

Different sessions may use different modes, but individual blocks may not override the session's control mode.

A session contains:

- Name.
- Optional description.
- Control mode.
- Ordered list of blocks.
- Optional session-level metadata for future expansion.

### ERG sessions

Each block specifies:

- Name.
- Duration in seconds.
- Target power in watts (currently 0–1000 W).
- Coaching instruction.
- Optional block category.

Use existing ERG control functionality.

At block boundaries, command the trainer to the new target power.

### Simulation sessions

Each block specifies:

- Name.
- Duration in seconds.
- Simulated gradient in percent.
- Coaching instruction.
- Optional block category.

For example:

| Block | Duration | Gradient | Instruction |
|---|---|---|---|
| Warm-up | 10 min | 0% | Easy pedaling, gradually increase cadence |
| Preparation | 30 sec | 2% | Shift into a comfortable sprinting gear |
| Sprint | 10 sec | 4% | Sprint! Maximum effort! |
| Recovery | 4 min | 0% | Recover, easy spinning |
| Sprint | 10 sec | 4% | Sprint! Try to match your previous power |
| Cooldown | 5 min | 0% | Relax and lower your heart rate |

Simulation mode must control the trainer through an actual supported slope/simulation command rather than translating gradients into fixed watts.

**Important hardware requirement:** Inspect `Sensors/BleTacxTrainer.cs`, the existing Tacx protocol code, and the available device-control capabilities before implementing Simulation mode.

Determine whether the original Tacx NEO Smart supports the required simulation command through the application's existing BLE control path.

Do not invent BLE opcodes, packet structures, or protocol support.

If slope control requires an additional protocol implementation, implement it only when its specification can be verified from reliable documentation or existing compatible code.

If simulation control cannot be implemented safely, preserve the complete Simulation session builder, persistence, and visualization, but disable hardware execution of Simulation sessions with a clear explanation. ERG execution must remain functional.

Do not substitute ERG control for simulation control.

## 3. Session builder

Create a dedicated reusable WinForms control for editing sessions.

The user should be able to:

- Create a new session.
- Select ERG or Simulation mode.
- Enter a session name and description.
- Add blocks.
- Edit blocks.
- Duplicate blocks.
- Remove blocks.
- Reorder blocks using Up/Down controls or drag-and-drop.
- Save and load sessions.
- Start a validated session.
- See the total workout duration.
- Preview the entire session visually.

### Block editor

The block editor displays fields appropriate to the selected session mode.

For ERG:

- Block name.
- Duration.
- Target watts.
- Coaching instruction.
- Block category.

For Simulation:

- Block name.
- Duration.
- Gradient percentage.
- Coaching instruction.
- Block category.

Use a multiline text input for coaching instructions.

Suggested categories:

- Warm-up
- Endurance
- Tempo
- Climb
- Sprint
- Recovery
- Cooldown
- Custom

Categories are primarily for presentation, colors, and user organization. They must not silently change resistance or power targets.

Validate:

- Empty session and block names.
- Missing blocks.
- Nonpositive durations.
- Unsupported target wattages.
- Unsupported gradients.
- Excessive total duration or arithmetic overflow.
- Invalid values in imported files.

Show validation errors to the user rather than silently correcting values.

## 4. Coaching instructions

Every block may have a coaching instruction that is displayed during execution.

The running session should always show:

**Current instruction**

Large, prominent text displaying the instruction associated with the active block.

Example:

> SPRINT! Give it everything for 10 seconds!

**Next instruction**

Smaller text showing the instruction associated with the next block.

Example:

> UP NEXT: Recovery — slow down and spin easily.

This allows the rider to anticipate upcoming actions.

### Instruction behavior

- Change the current instruction when a block becomes active.
- Immediately update the upcoming instruction.
- Show the next block's name and optionally its duration and target.
- If the next block has no instruction, still show its name and relevant target.
- For the final block, show an appropriate end-of-session indicator.
- Keep instructions visible while paused.
- Do not allow instruction rendering to block trainer communication or timing.

Use the same instruction system for both ERG and Simulation sessions.

### Upcoming block countdown

During the final few seconds of a block, visually emphasize that a transition is approaching.

For example:

- 5 seconds remaining: highlight upcoming instruction.
- 3 seconds remaining: show a clear countdown.
- Transition: activate the next block and update instructions.

Keep countdown behavior configurable through constants initially.

Do not automatically alter trainer resistance early merely because an instruction preview is shown.

## 5. Animated workout progress visualization

Implement a distinctive, playful progress visualization inspired by a side-scrolling platform game.

The workout is represented as a series of horizontal platforms or bars.

A small cartoon cycling character represents the rider's progress and **jumps between platforms when transitioning between blocks**.

The aesthetic should be colorful, polished, friendly, and readable rather than photorealistic.

Do not use copyrighted Nintendo or Mario sprites, characters, or assets. Create an original character using simple programmatic vector shapes or other self-contained assets.

### Workout platforms

Each training block is represented by a rectangular platform.

Platform properties:

- Width represents block duration.
- Height represents the block's wattage or gradient.
- Color represents the block category.
- A small label identifies the block.
- The currently active platform is visually highlighted.
- Completed platforms have a distinct completed appearance.
- Upcoming platforms remain visible.

Use separate vertical scales for ERG and Simulation sessions.

ERG platforms represent watts.

Simulation platforms represent gradient percentage.

Display a clearly labeled vertical scale so users do not confuse watts and gradients.

Handle zero and negative gradients correctly if supported by the trainer and model.

Use sensible minimum platform widths for very short blocks so sprint intervals remain visible.

The graph must remain horizontally scrollable or dynamically scaled for long workouts.

### Cartoon character

Create a small original cartoon character representing the rider.

A simple stylized cyclist, bicycle mascot, or similarly recognizable character is suitable.

The character should:

- Be positioned above the active platform.
- Advance horizontally as time passes through the active block.
- Jump toward the next platform at each block transition.
- Land on the new platform.
- Have a simple idle or pedaling animation.
- Pause movement when the session is paused.
- Stop animating when the session ends.

Use a smooth jump animation with an arc rather than instantly teleporting between platforms.

If the next platform is higher, the character jumps upward.

If it is lower, the character jumps downward.

Ensure the character remains within the visible graph area.

### Progress behavior

Time, not distance, determines the character's horizontal position.

The character must correspond to the active block and elapsed block time.

Consider a camera-following presentation:

- The character stays roughly within the central portion of the view.
- Platforms scroll horizontally as the workout advances.
- Some completed platforms remain visible behind the character.
- Upcoming platforms are visible ahead.

Provide enough visual context for the user to anticipate upcoming changes.

Do not make camera scrolling disorienting.

Also display a conventional overall progress indicator so the workout remains understandable without relying solely on animation.

### Implementation guidance

Use native WinForms drawing where practical:

- Custom `Control` or `UserControl`.
- `OnPaint` and GDI+ drawing.
- Double buffering to prevent flicker.
- Smooth animation using elapsed time.
- Carefully manage pens, brushes, fonts, and other disposable drawing resources.

Avoid adding a game engine, browser framework, or heavyweight graphical dependency.

Rendering must be entirely separate from workout timing and hardware control.

Frame-rate fluctuations must not affect session duration or block transitions.

Make the visualization usable even when the trainer is disconnected, for session previews.

For previews, show the platforms without running trainer commands.

## 6. Workout file formats

Use a native JSON format as the primary format for saving and loading sessions.

Suggested extension:

`.ergsession.json`

The native format must preserve:

- Session name.
- Description.
- Session mode.
- Block ordering.
- Block names.
- Exact durations in seconds.
- ERG watt targets or Simulation gradient targets.
- Coaching instructions.
- Block categories.
- Schema version.

Use explicit serialization models and a version field so the format can evolve.

Avoid coupling JSON persistence to WinForms controls or hardware objects.

### Example ERG session

```json
{
  "schemaVersion": 1,
  "name": "Power Intervals",
  "description": "Short high-power intervals",
  "mode": "erg",
  "blocks": [
    {
      "name": "Warm-up",
      "durationSeconds": 600,
      "targetWatts": 120,
      "category": "warmup",
      "instruction": "Easy pedaling. Prepare for the intervals."
    },
    {
      "name": "Power interval",
      "durationSeconds": 60,
      "targetWatts": 300,
      "category": "sprint",
      "instruction": "Hold the target power for one minute."
    },
    {
      "name": "Recovery",
      "durationSeconds": 180,
      "targetWatts": 100,
      "category": "recovery",
      "instruction": "Recover. Breathe steadily."
    }
  ]
}
```

### Example Simulation session

```json
{
  "schemaVersion": 1,
  "name": "Sprint Training",
  "description": "Short sprints against a simulated incline",
  "mode": "simulation",
  "blocks": [
    {
      "name": "Warm-up",
      "durationSeconds": 600,
      "gradientPercent": 0,
      "category": "warmup",
      "instruction": "Warm up with comfortable pedaling."
    },
    {
      "name": "Sprint preparation",
      "durationSeconds": 30,
      "gradientPercent": 2,
      "category": "endurance",
      "instruction": "Get ready. Select your sprinting gear."
    },
    {
      "name": "Sprint",
      "durationSeconds": 10,
      "gradientPercent": 4,
      "category": "sprint",
      "instruction": "SPRINT! Maximum effort!"
    },
    {
      "name": "Recovery",
      "durationSeconds": 240