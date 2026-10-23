using System.Text.Json;

namespace MeiOS;

public static class SelfTests
{
    private static readonly List<object> Results = [];
    private static void Check(string name, bool condition, string actual)
    {
        Results.Add(new { name, passed=condition, actual });
        if (!condition) throw new InvalidOperationException("Тест не пройден: " + name + " / " + actual);
    }
    public static SimulationModel Empty(int quantum=4, int duration=3)
    {
        var config=new ModelConfig { Slots=8, Memory=new(512,512,1), TaskSize=new(64,64,1), Quantum=quantum, AgingTicks=3, LoadTicks=0, DispatchTicks=0, IoInitTicks=0, IoInterruptTicks=0, FinishTicks=0, IoDuration=duration };
        var m=new SimulationModel(config);
        m.Initialize();
        m.AutoLoad=false;
        Array.Clear(m.Table);
        foreach(var q in m.ReadyQueues) q.Clear();
        m.Cpu.Active=null; m.SystemJobs.Clear(); m.Io.Clear();
        return m;
    }
    public static Psw Insert(SimulationModel m, int size, Command[]? commands=null)
    {
        m.SetPendingTask(size,commands);
        if(!m.LoadNewTask()) throw new InvalidOperationException("Ошибка тестовой загрузки");
        return m.Residents.Last();
    }
    public static void Run()
    {
        // Интерфейс: узкие окна, все экраны, длинные строки и границы прокрутки.
        var ui = Empty();
        Insert(ui, 64); Insert(ui, 200);
        ui.Log(new string('Ж', 500));
        foreach (var process in ui.Residents) { process.Id = 12345678 + process.Slot; process.Pc = int.MaxValue; }
        ui.Cpu.Counter = int.MaxValue;
        bool layoutOk = true;
        foreach (int width in new[] { 1, 40, 79, 119, 179 })
        foreach (int height in new[] { 5, 10, 24, 40 })
        for (int view = 0; view < 3; view++)
        {
            int actualScroll = -1;
            var frame = Dashboard.Build(ui, "task " + new string('9', 100), width, height, view, int.MaxValue, true, value => actualScroll = value);
            layoutOk &= frame.Count == height && frame.All(line => line.Length <= width) && actualScroll >= 0 && actualScroll < int.MaxValue;
            if (width >= 5) layoutOk &= frame[^1].EndsWith('9');
            frame = Dashboard.Build(ui, "", width, height, view, -100);
            layoutOk &= frame.Count == height && frame.All(line => line.Length <= width);
        }
        Check("dashboard_layout", layoutOk, "all views, 20 window sizes, long IDs/log/input, scroll limits");
        var fullScreen = Empty();
        for (int i = 0; i < 8; i++) Insert(fullScreen, 64);
        var wideFrame = Dashboard.Build(fullScreen, width: 180, height: 48);
        Check("all_parameters_one_screen", !wideFrame.Any(line => line.StartsWith("Строки "))
            && wideFrame.Any(line => line.Contains("полезная загрузка"))
            && wideFrame.Any(line => line.Contains("Пропускная способность")),
            $"frame={wideFrame.Count}x{wideFrame[0].Length}, residents={fullScreen.Residents.Count()}");
        var meansModel = Empty();
        Insert(meansModel, 64, [new(Op.Compute), new(Op.Exit)]);
        var done = new Psw
        {
            Id = 99, Size = 128, Program = [new(Op.Compute), new(Op.Io), new(Op.Compute), new(Op.Exit)],
            IoCount = 1, State = ProcessState.Completed, ReadyTicks = 6, ActiveTicks = 4,
            IoTicks = 3, SystemTicks = 2, Arrival = 10, End = 20
        };
        meansModel.Completed.Add(done);
        var means = new List<string>();
        Dashboard.DrawMeans(meansModel, means);
        Check("input_output_means", means[0].Contains("размер 96.0") && means[0].Contains("команд 3.0")
            && means[1].Contains("готовность 6.0") && means[1].Contains("время в системе 10.0"), string.Join(" | ", means));
        var m=Empty();
        Insert(m,200); Insert(m,300);
        m.SetPendingTask(20);
        Check("memory_reject",!m.LoadNewTask() && m.FreeMemory==12,$"free={m.FreeMemory}");
        m=Empty();
        for(int i=0;i<8;i++) Insert(m,1);
        m.SetPendingTask(1);
        Check("psw_capacity",!m.LoadNewTask() && m.FreeMemory==504,$"slots={m.Residents.Count()}");
        m=Empty(); var p=Insert(m,64);
        m.Dispatch(); m.ExecuteOneTick();
        Check("hardware_pc",m.Cpu.Counter==1 && p.Pc==1,$"pc={p.Pc}");
        m.ExecuteDirective("speed 1000"); m.ChangeSpeed(1.1);
        Check("speed_max",m.Speed==1000,$"speed={m.Speed}");
        m.ExecuteDirective("speed 0.1"); m.ChangeSpeed(0.9);
        Check("speed_min",m.Speed==0.1,$"speed={m.Speed}");
        m.ExecuteDirective("speed 100"); m.ExecuteDirective("+");
        Check("speed_plus",Math.Abs(m.Speed-110)<1e-9,$"speed={m.Speed}");
        m.ExecuteDirective("speed 100"); m.ExecuteDirective("-");
        Check("speed_minus",Math.Abs(m.Speed-90)<1e-9,$"speed={m.Speed}");
        var a=new SimulationModel(); var b=new SimulationModel(); a.Initialize(); b.Initialize();
        Check("seed_repeat",string.Join('|',a.Residents)==string.Join('|',b.Residents),string.Join('|',a.Residents));
        var computeOnly=Enumerable.Repeat(new Command(Op.Compute),30).Append(new Command(Op.Exit)).ToArray();
        m=Empty(2); var first=Insert(m,64,computeOnly); var second=Insert(m,64,computeOnly); m.Dispatch();
        m.ExecuteOneTick(); m.ExecuteOneTick();
        Check("rr_equal_priority",m.Cpu.Active==second,$"active={m.Cpu.Active?.Id}");
        m=Empty(20); first=Insert(m,64,computeOnly); m.Dispatch(); second=Insert(m,300,computeOnly);
        m.ExecuteOneTick();
        Check("relative_no_preemption",m.Cpu.Active==first,$"active={m.Cpu.Active?.Id}, new_priority={second.Priority}");
        m=Empty(); first=Insert(m,64); second=Insert(m,300);
        Check("large_jobs_priority",m.GetNextProcess()==second,$"base small={first.BasePriority}, large={second.BasePriority}");
        for(int i=0;i<6;i++) m.AgeReadyProcesses();
        Check("aging",first.Priority==2,$"priority={first.Priority}");
        m=Empty(100); p=Insert(m,64,[new(Op.Compute),new(Op.Exit)]);
        m.ExecuteOneTick(); m.ExecuteOneTick();
        Check("compute_exit_memory",p.Memory[2]==5 && m.Completed.Count==1 && m.UsedMemory==0,$"result={p.Memory[2]}, completed={m.Completed.Count}, used={m.UsedMemory}");
        m=Empty(100); p=Insert(m,64,[new(Op.Compute,99),new(Op.Exit)]);
        m.ExecuteOneTick();
        Check("memory_protection",p.State==ProcessState.Killed && m.UsedMemory==0,$"state={p.State}");
        m=Empty(100,3); p=Insert(m,64,[new(Op.Io),new(Op.Exit)]);
        m.ExecuteOneTick();
        Check("io_block",p.State==ProcessState.BlockedIo && m.Io.Single().Left==3,$"state={p.State}, IO_left={m.Io.Single().Left}");
        for(int i=0;i<4;i++) m.ExecuteOneTick();
        Check("io_interrupt_completion",p.State==ProcessState.Completed && p.End==5,$"state={p.State}, end={p.End}");
        var screen = Dashboard.Build(m, width: 79, height: 24);
        Check("stable_dashboard",screen.Count == 24 && screen.All(line => line.Length <= 79) && screen[^1].Contains("Q выход"), $"lines={screen.Count}, controls fixed");
        Console.WriteLine(JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
