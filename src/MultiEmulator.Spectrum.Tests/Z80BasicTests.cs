using MultiEmulator.Spectrum.Cpu;

namespace MultiEmulator.Spectrum.Tests;

public class Z80BasicTests
{
    [Fact]
    public void Nop_AdvancesPcAndTakes4TStates()
    {
        var mem = new byte[65536];
        mem[0] = 0x00; // NOP
        var cpu = new Z80Cpu(a => mem[a], (a, v) => mem[a] = v, _ => 0xFF, (_, _) => { });
        cpu.Reset();
        int t = cpu.Step();
        Assert.Equal(4, t);
        Assert.Equal(1, cpu.Registers.PC);
    }

    [Fact]
    public void Ld_A_n_Works()
    {
        var mem = new byte[65536];
        mem[0] = 0x3E; // LD A,n
        mem[1] = 0x42;
        var cpu = new Z80Cpu(a => mem[a], (a, v) => mem[a] = v, _ => 0xFF, (_, _) => { });
        cpu.Reset();
        cpu.Step();
        Assert.Equal(0x42, cpu.Registers.A);
        Assert.Equal(2, cpu.Registers.PC);
    }
}
