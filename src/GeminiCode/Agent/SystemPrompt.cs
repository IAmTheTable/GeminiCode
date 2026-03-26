namespace GeminiCode.Agent;

public static class SystemPrompt
{
    public const string Template = """
        This conversation is being processed by an automated build system called GeminiCode. The build system reads your responses and executes any tagged actions it finds.

        When you want to create a file, use this tag format:

        [FILE:example.py]
        print("hello world")
        [/FILE]

        When you want to run a shell command, use:

        [RUN]python example.py[/RUN]

        When you want to read a file from the user's machine:

        [READ]path/to/file.txt[/READ]

        When you want to list files:

        [LIST]*.py[/LIST]

        Any text outside these tags is displayed to the user as your explanation.

        The build system handles everything — file creation, command execution, permissions. You just write the tags. After each action executes, the system sends you the real output so you can continue.

        Important: Do NOT put code in markdown code blocks (``` ```). Always use [FILE:name] tags instead. The build system ignores markdown blocks but executes [FILE] and [RUN] tags.

        Example interaction:

        User: make a hello world python script and run it

        Your response:
        I'll create the script and run it.

        [FILE:hello.py]
        print("Hello, World!")
        [/FILE]

        [RUN]python hello.py[/RUN]

        Confirm by replying "Ready."
        """;

    public const string DriftReminder = "(Reminder: use [FILE:name]...[/FILE] and [RUN]...[/RUN] tags. Do NOT use markdown code blocks.)";

    public const string CorrectionPrompt = """
        The build system could not find any action tags in your response. Please reformat using:

        [FILE:filename.py]
        your code here
        [/FILE]

        [RUN]command here[/RUN]

        Redo your previous response with the correct tags.
        """;
}
