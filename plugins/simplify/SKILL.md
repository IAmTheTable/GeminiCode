---
name: simplify
description: Review changed code for reuse, simplification, and efficiency, then apply fixes
command: /simplify
argHint:
---
## Phase: Analyzing changes
I'm going to review recent code changes. First, let me see what changed.

[GIT]diff[/GIT]

[GIT]diff --cached[/GIT]

## Phase: Code Reuse Review
Review the following diff for code reuse opportunities:

{phase1_result}

For each change:
1. Search for existing utilities and helpers that could replace newly written code. Look for similar patterns elsewhere in the codebase.
2. Flag any new function that duplicates existing functionality. Suggest the existing function to use instead.
3. Flag any inline logic that could use an existing utility — hand-rolled string manipulation, manual path handling, custom environment checks, etc.

Search the codebase with [GREP] to find existing patterns before making claims. Be specific — cite file paths and function names.

## Phase: Code Quality Review
Review the same changes for code quality issues:

{phase1_result}

Look for:
1. Redundant state that duplicates existing state or cached values that could be derived
2. Parameter sprawl — adding new parameters instead of restructuring
3. Copy-paste with slight variation that should be unified
4. Leaky abstractions — exposing internal details that should be encapsulated
5. Stringly-typed code where constants or enums already exist in the codebase
6. Unnecessary comments explaining WHAT (keep only non-obvious WHY)

Be specific — cite line numbers and suggest fixes.

## Phase: Efficiency Review
Review the same changes for efficiency:

{phase1_result}

Look for:
1. Unnecessary work: redundant computations, repeated file reads, duplicate API calls, N+1 patterns
2. Missed concurrency: independent operations that could run in parallel
3. Hot-path bloat: blocking work on startup or per-request paths
4. Recurring no-op updates: state updates that fire unconditionally without change detection
5. Unnecessary existence checks before operating (TOCTOU anti-pattern)
6. Memory: unbounded data structures, missing cleanup, event listener leaks
7. Overly broad operations: reading entire files when only a portion is needed

Be specific — cite line numbers and suggest fixes.

## Phase: Applying fixes
Here are the review findings from the previous phases:

**Code Reuse:**
{phase2_result}

**Code Quality:**
{phase3_result}

**Efficiency:**
{phase4_result}

Fix each valid issue directly using [EDIT:] tags. If a finding is a false positive or not worth addressing, skip it. Do not argue with the findings — just fix or skip.
