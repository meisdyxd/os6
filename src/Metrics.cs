namespace MeiOS;

public sealed partial class SimulationModel
{
    public void IncreaseReadyTime(int ticks)
    {
        foreach (var p in Residents.Where(p => p.State == ProcessState.Ready)) p.ReadyTicks += ticks;
    }

}
