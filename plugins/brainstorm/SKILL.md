---
name: brainstorm
description: Turn ideas into fully formed designs through guided dialogue
command: /brainstorm
argHint: <topic or feature description>
---
## Phase: Understanding context
The user wants to brainstorm a feature or idea: {input}

First, explore the current project context:
1. Check the project structure with [TREE:depth=2][/TREE]
2. Check recent git history with [GIT]log -10 --oneline[/GIT]
3. Look at relevant existing code based on what the user described

Then ask 3-5 clarifying questions to understand what they want to build, constraints (performance, compatibility, timeline), and success criteria. Present questions as a numbered list. Be specific to the project.

## Phase: Exploring approaches
Based on what you've learned about the project, propose 2-3 different approaches to implement the feature.

For each approach: name it clearly, describe the architecture (which files, what components, how data flows), list pros and cons, and estimate relative complexity. Lead with your recommended approach and explain why. Reference existing code patterns where relevant.

## Phase: Designing solution
Based on the recommended approach, create a detailed design:
1. File structure — which files to create or modify, what each is responsible for
2. Component interfaces — key classes/functions, their signatures, how they connect
3. Data flow — how data moves through the system
4. Error handling — what can go wrong and how to handle it
5. Testing strategy — what to test and how

Be specific about file paths relative to the project root. Show key interfaces and type definitions.

## Phase: Summary
Summarize the brainstorming session:
1. What we're building: one paragraph
2. Chosen approach: the recommended approach and why
3. Key design decisions: bullet list
4. Next steps: what to implement first

Keep it concise — this summary will be saved for reference.
