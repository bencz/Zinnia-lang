#include <stdio.h>

extern "C"
{
	FILE *System_StandardC_stdin_get();
	FILE *System_StandardC_stdout_get();
	FILE *System_StandardC_stderr_get();
}

FILE *System_StandardC_stdin_get()
{
	return stdin;
}

FILE *System_StandardC_stdout_get()
{
	return stdout;
}

FILE *System_StandardC_stderr_get()
{
	return stderr;
}