using Newtonsoft.Json;
using ToolUse.Core;
using ToolUse.Core.RL;

const int episodes = 10000;
const int maxSteps = 100;
const int gridSize = 10;

QTable seekerQ = new();
QTable hiderQ = new();

QAgent seekerAgent = new(seekerQ, epsilon: 0.1f);
QAgent hiderAgent = new(hiderQ, epsilon: 0.1f);

int caughtCount = 0;
Random rand = new();

for (int ep = 0; ep < episodes; ep++)
{
    World world = new(gridSize);
    Agent seeker = world.AgentA;
    Agent hider = world.AgentB;

    for (int step = 0; step < maxSteps; step++)
    {
        // ==== STATE до действий ====
        var seekerState = new State(seeker.X, seeker.Y, hider.X, hider.Y, seeker.CanSee(hider, world));
        var hiderState = new State(hider.X, hider.Y, seeker.X, seeker.Y, seekerState.IsVisible);

        // ==== ВЫБОР ДЕЙСТВИЙ ====
        int seekerAction = seekerAgent.ChooseAction(seekerState);
        int hiderAction = hiderAgent.ChooseAction(hiderState);

        var (sdx, sdy) = Actions.AllMoves[seekerAction];
        var (hdx, hdy) = Actions.AllMoves[hiderAction];

        // ==== ПЕРЕМЕЩЕНИЕ ====
        int sx = seeker.X + sdx;
        int sy = seeker.Y + sdy;
        if (!world.IsBlocked(sx, sy)) seeker.Move(sdx, sdy, world);

        int hx = hider.X + hdx;
        int hy = hider.Y + hdy;
        if (!world.IsBlocked(hx, hy)) hider.Move(hdx, hdy, world);

        // ==== НОВОЕ СОСТОЯНИЕ ====
        var newSeekerState = new State(seeker.X, seeker.Y, hider.X, hider.Y, seeker.CanSee(hider, world));
        var newHiderState = new State(hider.X, hider.Y, seeker.X, seeker.Y, newSeekerState.IsVisible);

        // ==== НАГРАДЫ ====
        bool caught = seeker.X == hider.X && seeker.Y == hider.Y;
        float seekerReward = caught ? 10f : -1f;
        float hiderReward = caught ? -10f : 1f;

        // ==== ОБНОВЛЕНИЕ Q ====
        seekerAgent.Update(seekerState, seekerAction, seekerReward, newSeekerState);
        hiderAgent.Update(hiderState, hiderAction, hiderReward, newHiderState);

        if (caught)
        {
            caughtCount++;
            break;
        }
    }

    if ((ep + 1) % 1000 == 0)
    {
        Console.WriteLine($"Episode {ep + 1}/{episodes} | Caught: {caughtCount}");
    }
}

// ==== СОХРАНЕНИЕ ОБЕИХ Q-ТАБЛИЦ ====
string seekerPath = "qtable_seeker.json";
string hiderPath = "qtable_hider.json";

File.WriteAllText(seekerPath, JsonConvert.SerializeObject(seekerQ.AsDictionary(), Formatting.Indented));
File.WriteAllText(hiderPath, JsonConvert.SerializeObject(hiderQ.AsDictionary(), Formatting.Indented));

Console.WriteLine("\n✅ Обе Q-таблицы сохранены.");
