// Direct regression test for the "doesn't type the word" bug: SendInput
// silently drops every event when the marshaled INPUT struct size doesn't
// exactly match the real Win32 sizeof(INPUT) on x64 (40 bytes). This asserts
// the struct layout stays correct so the bug can't silently come back.
using WordBombTool;
using Xunit;

namespace WordBombTool.Tests;

public class InputSimulatorTests
{
    [Fact]
    public void NativeInputStructSize_MatchesWin32SizeOfInputOnX64()
    {
        Assert.Equal(40, InputSimulator.NativeInputStructSizeBytes);
    }
}
