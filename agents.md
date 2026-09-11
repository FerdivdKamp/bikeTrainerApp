# AGENTS.md

## Project purpose

PowerTrainer is a personal hobby project written in .NET and runs as a Windows Service.

The project is used to experiment with and learn about:

- .NET worker services
- Windows Services
- Background processing
- Indoor cycling / trainer integrations
- Device communication
- Workout and telemetry processing

An important goal of this project is learning. Prefer helping me understand the implementation over completing large amounts of work autonomously.

## How to work with me

- Act as a pair programmer, not as an autonomous project owner.
- Explain significant design decisions before making substantial changes.
- Keep changes small, focused, and easy to review.
- Do not implement unrelated improvements unless I explicitly ask for them.
- Do not refactor unrelated code as part of another task.
- Prefer simple, idiomatic .NET solutions over clever abstractions.
- Avoid over-engineering for hypothetical future requirements.
- When multiple approaches are reasonable, explain the trade-offs briefly.
- If a change introduces a pattern or API that may not be obvious, explain why it is being used.

For larger changes:

1. Inspect the relevant code first.
2. Explain the current behavior.
3. Describe the proposed change.
4. Identify important trade-offs or risks.
5. Wait for approval if the change significantly affects architecture.
6. Implement the change in small steps.

## Technology

This project is written in C# using modern .NET.

Prefer:

- Current idiomatic C#
- Dependency injection
- `Microsoft.Extensions.Hosting`
- `Microsoft.Extensions.Logging`
- `Microsoft.Extensions.Options`
- Async APIs where I/O is involved
- Cancellation tokens for long-running or background operations

Do not introduce third-party packages unless they provide a clear benefit over the .NET standard libraries or existing project dependencies.

Before suggesting a new NuGet package, check whether the existing project already has suitable functionality.

## Windows Service behavior

This application runs as a long-lived Windows Service.

Code should therefore assume that:

- The process may run continuously for long periods.
- The service should shut down cleanly.
- Background work must respect cancellation.
- Transient failures should not unnecessarily terminate the whole service.
- Unexpected exceptions should be logged with useful context.
- Resource cleanup is important.
- Retry loops must not spin aggressively.
- Long-running loops should not block threads unnecessarily.

Prefer cancellation-aware patterns such as:

    await Task.Delay(delay, stoppingToken);

rather than blocking calls such as:

    Thread.Sleep(...);

Background services should normally respect the `CancellationToken` passed to `ExecuteAsync`.

Do not swallow exceptions silently.

## Async programming

Use async/await for I/O-bound operations.

Guidelines:

- Avoid `.Result`, `.Wait()`, and `.GetAwaiter().GetResult()` unless there is a specific reason.
- Propagate `CancellationToken` where practical.
- Do not create unnecessary `Task.Run` wrappers around naturally asynchronous work.
- Avoid fire-and-forget tasks unless their lifecycle and exception handling are explicitly managed.
- Do not use `async void` except for event handlers where required.

## Dependency injection

Prefer constructor injection.

Services should have clear responsibilities.

Avoid:

- Service locator patterns.
- Calling `IServiceProvider.GetService()` throughout application logic.
- Very large service classes with unrelated responsibilities.

Use appropriate service lifetimes and be careful when using scoped services from long-running background services.

## Configuration

Use the .NET configuration system.

Prefer strongly typed settings through:

- `IOptions<T>`
- `IOptionsSnapshot<T>`
- `IOptionsMonitor<T>`

where appropriate.

Do not hard-code:

- Machine-specific paths
- Credentials
- API keys
- Bluetooth/device identifiers
- Ports
- Environment-specific URLs

Secrets must never be committed to source control.

If introducing new configuration, document the required setting.

## Logging

Use `ILogger<T>`.

Prefer structured logging:

    _logger.LogInformation(
        "Connected to trainer {TrainerName} with device id {DeviceId}",
        trainerName,
        deviceId);

rather than string interpolation:

    _logger.LogInformation($"Connected to trainer {trainerName}");

Log useful context, but avoid excessive logging in high-frequency telemetry loops.

Use log levels consistently:

- `Trace` / `Debug`: detailed diagnostics and high-frequency technical information
- `Information`: normal lifecycle events and important state changes
- `Warning`: unexpected but recoverable situations
- `Error`: failed operations requiring attention
- `Critical`: service-level failure

Do not log secrets or sensitive authentication data.

## Cycling and trainer domain logic

Keep cycling calculations and trainer communication separated where practical.

Prefer separating concerns such as:

- Trainer/device communication
- Telemetry ingestion
- Workout state
- Power/cadence/heart-rate data
- Workout calculations
- Persistence
- Service orchestration

Pure calculations should preferably be implemented as deterministic methods or domain services that can be unit tested without hardware.

Avoid coupling calculations directly to:

- Windows Service lifecycle code
- Bluetooth/network transport
- Logging
- Persistence

Where possible, transform incoming device data into domain models before applying calculations.

## Device communication

Hardware and network communication should be treated as unreliable.

Account for:

- Temporary disconnects
- Timeouts
- Malformed or missing data
- Reconnects
- Cancellation
- Devices disappearing during operation

Retries should:

- Have a bounded or sensible delay.
- Respect cancellation.
- Avoid tight retry loops.
- Produce useful logging.

Do not hide connection failures indefinitely.

Prefer explicit connection-state handling over scattered boolean flags where this improves clarity.

## Performance

Trainer telemetry may arrive frequently.

Avoid unnecessary work in high-frequency paths.

Be mindful of:

- Excessive object allocation
- Excessive logging
- Unnecessary LINQ in very hot loops
- Blocking operations
- Writing to disk or databases for every telemetry event

However, do not prematurely optimize code that is not performance-sensitive.

Readable code takes priority unless profiling shows a problem.

## Tests

Add or update tests when behavior changes.

Prioritize tests for:

- Cycling calculations
- State transitions
- Parsing
- Device message handling
- Edge cases
- Retry or timeout behavior where practical

Prefer deterministic unit tests.

Tests should not require real cycling hardware unless they are explicitly integration tests.

When fixing a bug, add a regression test when practical.

Run the relevant tests after making changes.

## Code style

Follow the existing conventions in the repository.

Prefer:

- Descriptive names
- Small focused methods
- Early returns where they improve readability
- Nullable reference types where already enabled
- Pattern matching where it improves clarity
- `var` when the type is obvious from the right-hand side
- Explicit types when they make code easier to understand

Avoid:

- Deeply nested conditionals
- Giant methods
- Unnecessary regions
- Speculative abstractions
- Interfaces with only one implementation unless there is a clear reason for the abstraction

Comments should explain **why**, not restate what the code obviously does.

## Architecture

Preserve the existing architecture unless the requested task requires changing it.

Before introducing:

- A new project
- A new architectural layer
- A message bus
- A database
- A repository abstraction
- CQRS
- MediatR
- A plugin system
- Complex factories

first explain why the added complexity is justified.

Prefer the smallest architecture that cleanly supports the current requirements.

## Error handling

Handle errors at the level where useful action can be taken.

Do not use exceptions for normal control flow.

When catching an exception:

- Preserve useful context.
- Log it at the appropriate boundary.
- Avoid logging the same exception repeatedly at multiple layers.
- Either recover meaningfully or let it propagate.

Do not write empty `catch` blocks.

## Git

Do not:

- Commit changes
- Push changes
- Create branches
- Rewrite history
- Reset files
- Modify `.gitignore`

unless I explicitly ask.

Keep code changes limited to files relevant to the requested task.

Do not reformat unrelated files.

## Before changing code

Before making significant changes:

- Inspect the relevant implementation.
- Inspect related interfaces and models.
- Inspect existing tests.
- Look for existing conventions before inventing a new pattern.

Do not assume how a component works purely from its name.

## After changing code

When finished:

1. Summarize what changed.
2. Explain important implementation decisions.
3. Mention any assumptions.
4. Mention tests that were added or updated.
5. Run relevant build/tests where possible.
6. Report any warnings, failures, or remaining uncertainties.

Do not claim that code works if it has not been built or tested.