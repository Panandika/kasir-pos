using Kasir.Avalonia.Forms.Help;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms.Help;

[TestFixture]
public class BantuanViewModelTests
{
    [Test]
    public void Default_StartsInIdleTanya()
    {
        var vm = new BantuanViewModel();
        Assert.That(vm.State, Is.EqualTo(BantuanState.Idle));
        Assert.That(vm.Mode, Is.EqualTo(BantuanMode.Tanya));
        Assert.That(vm.IsTanya, Is.True);
        Assert.That(vm.IsLapor, Is.False);
        Assert.That(vm.ModeLabel, Is.EqualTo("TANYA"));
    }

    [Test]
    public void ToggleMode_FlipsModeAndPlaceholder()
    {
        var vm = new BantuanViewModel();
        vm.ToggleMode();
        Assert.That(vm.Mode, Is.EqualTo(BantuanMode.Lapor));
        Assert.That(vm.ModeLabel, Is.EqualTo("LAPOR"));
        Assert.That(vm.Placeholder, Does.Contain("Jelaskan"));

        vm.ToggleMode();
        Assert.That(vm.Mode, Is.EqualTo(BantuanMode.Tanya));
    }

    [Test]
    public void ToggleMode_WithInput_TransitionsToComposing()
    {
        var vm = new BantuanViewModel();
        vm.Input = "printer macet";
        vm.ToggleMode();
        Assert.That(vm.State, Is.EqualTo(BantuanState.Composing));
    }

    [Test]
    public void ToggleMode_NoInput_StaysIdle()
    {
        var vm = new BantuanViewModel();
        vm.ToggleMode();
        Assert.That(vm.State, Is.EqualTo(BantuanState.Idle));
    }

    [Test]
    public void Reset_ClearsAllAndReturnsToIdleTanya()
    {
        var vm = new BantuanViewModel();
        vm.Mode = BantuanMode.Lapor;
        vm.Input = "test";
        vm.AnswerTitle = "x";
        vm.TicketNo = "TKT-1";
        vm.State = BantuanState.Sent;

        vm.Reset();

        Assert.That(vm.State, Is.EqualTo(BantuanState.Idle));
        Assert.That(vm.Mode, Is.EqualTo(BantuanMode.Tanya));
        Assert.That(vm.Input, Is.Empty);
        Assert.That(vm.AnswerTitle, Is.Empty);
        Assert.That(vm.TicketNo, Is.Empty);
    }

    [Test]
    public void Suggestions_PrefilledWithFourEntries()
    {
        var vm = new BantuanViewModel();
        Assert.That(vm.Suggestions.Count, Is.EqualTo(4));
    }
}
