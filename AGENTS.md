# AGENTS.md

This file defines project-wide guidance for code generation in this repository.

## Global policy
- Prefer minimal, local changes over broad refactors unless explicitly requested.
- Do not pre-extract a function that has only one call site. The exception is when the function has an explicit domain meaning; implementation-level concerns such as collection handling, pooling, release/lifetime mechanics, or helper plumbing are not enough.
- Keep helper scope as narrow as possible. If a helper is only meaningful inside one branch or one method, prefer a local function over a class-level method.
- Preserve existing code style and naming conventions in the touched area.
- Before changing rendering behavior, prefer checking how the current scene or pipeline already provides the needed data.
- Runtime code must not gain complexity for editor-only or test-only needs. Keep editor/test code physically close to the adjacent runtime code when practical, preferably in the same file, but keep it logically strict: runtime code must not reference editor/test definitions, and editor/test-only definitions must stay isolated behind explicit editor/test boundaries.
- If some type can be inferenced, use `var`.
- Use declarative way(LINQ), instead of imperative way. The only exception is performance-critial part(like Update()).
- Prefer built-in declarative collection operations such as `Array.IndexOf`, `Array.FindIndex`, `Contains`, or LINQ over handwritten search loops, unless the code is performance-critical.
- If `private` is default, do not mention it explicitly.
- If null has not special meaning in the context, do not check null just for validation. 
- Do not add defensive guards for required serialized/configured references. If the reference is required by the current mode, let direct access or assertions expose misconfiguration early.
- Early exception(crash) is always preferred than silent fail.
- Defensive/recovery code is only for genuinely uncertain inputs crossing a trust boundary: user-provided files, disk IO, network or IPC responses. Validate and handle those gracefully.
    - When such an exception occurs, never fall through into the normal success/follow-up path. Passing a half-built or null state downstream just relocates the failure and hides its cause; stop the flow at the failure point.
    - The exception must be reported through the reporting system (the `logMessageReceived`/`push_error` path, or an explicit report call), not silently swallowed.
    - The app must never be left hung or at a dead end. Resolve any pending awaiter/continuation the failed operation owns (e.g. `awaiter.set(null)`) so waiting UI can finish, and route to a recoverable state (retry or error screen) rather than an indefinite spinner.
- Internal invariant violations and resource integrity breaks (missing/renamed UXML elements, misconfigured assets, our own logic bugs) are programmer errors, not runtime conditions. Surface them as fast and loud as possible via assertion (or let direct access throw) so they are caught before shipping; never silently guard or fall back around them.
- Do not make state(or field) or cache of which can be calculated or queried. The only exception is performance critical part.
- `public static Instance`-style singleton access is a dark pattern and must never be used.
- Adding entries under `Resources` is allowed only for optional loading where one specific item is selected conditionally from multiple resources of the same type. Otherwise, use symbolic hard references for assets, ScriptableObject data, references to JSON/text files, or hardcoded values.
- Use just public, instead of private [serializefield].
- `{get; set;}` is prefered than `get_position()` and `set_position()`. If we are just wrapping something, just expose it as public.
- Default parameter of methods or functions are prohibited. Ask first.
- No Enum arithmetic. No adding, subtraction, comparing is not allowed between enums. Only equality comparison is allowed. 
- Early return is not recommended in usual case. If a certain flow needs it, ask first.
    - If an assumtion we relly on, make an assertion despite of silent fail or return. 
    - If the runtime condition is not matched, check and pass, not return immediatly.

# UI-toolkit
- Dedicated stylesheet is preferred than inline styling. 
- If some style is repeated, consider makeing common class or promoting it to global style(styles.uss)

## style
- snake_case at every identifiers(class, method, field, variable, enum...).
- Use the shortest name that preserves meaning.
- Avoid nested tautology when naming variables or functions. A value or operation inside a `somename` closure/context should not repeat `somename` unless the repetition adds meaning.
- If the context already makes the meaning clear, one-letter names are enough. For example, in `connections.Select(c => job(c))`, `c` is clearly a connection and should not be given extra meaning.
- Use attached opening delimiters.
  Opening `{`, `(`, and `[` must stay on the same line as the construct or expression that introduces them.
  Do not put opening delimiters on their own line.
- Even if only one exist after `if`, wrap it with brace.
- Write comments in English, concise. Omit the comment when the code is self-explanatory; comment only non-obvious intent or reasoning.

## Safety
- Always show plan before excute. The plan must includes scope of change.
- If a requested change appears to require a wider pipeline or architecture change, stop and ask before expanding scope.
- Do not remove or overwrite unrelated user changes.
- If verification was not run, state that clearly in the final response.
