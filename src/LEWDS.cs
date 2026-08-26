using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services;

namespace Leaves_LEWDS;

// Raises loot spawn probabilities that fall below a configured floor. Registers an AddTransformer
// that runs when a map's loose loot is read. LazyLoad<LooseLoot>.Value applies transformers in
// registration order (= OnLoad order = TypePriority order), so priority decides who gets the last
// word. This floor runs BEFORE LootFuckery (which sits deliberately higher, at +95000): the floor is
// applied first, then LootFuckery's probability edits are the FINAL pass. That ordering is
// intentional - it lets loot.jsonc pull specific items (keycards) BELOW this floor, while everything
// LootFuckery leaves at x1.0 keeps the floor. (The original TS mod ran this dead-last; we reversed
// that on purpose so keycard reductions aren't clamped back up - see LootFuckery's priority comment.)
// +90000 stays well inside the PostDBModLoader stage (next stage starts at +100000).
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 90000)]
public class LEWDS(
    DatabaseService databaseService,
    ModHelper modHelper)
    : IOnLoad
{
    private LewdsConfig config = null!;
    private Dictionary<MongoId, TemplateItem> itemDB = null!;

    public Task OnLoad()
    {
        string modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        config = modHelper.GetJsonDataFromFile<LewdsConfig>(modPath, "config/config.jsonc");
        itemDB = databaseService.GetItems();

        Leaves.Utils.Instance.printColor("[L.E.W.D.S.] Leaves - L.E.W.D.S. Starting:");

        // Registering a transformer costs nothing upfront - the actual deserialize cost only gets
        // paid when a raid naturally loads that map's loot, same as LootFuckery's own loot editing.
        // LogAtStartup opts back into eagerly reading every map's loose loot right now instead of
        // waiting for that natural read, purely so the "Found N spawns" line shows up in the
        // startup log immediately rather than the first time each map actually gets played.
        // location.LooseLoot is a LazyLoad<LooseLoot> with zero caching (unlike SPT 3.x, which
        // kept the whole DB in memory) - every .Value access re-deserializes that map's often
        // multi-MB loot JSON from disk. This is why LogAtStartup is visibly slower per map than
        // the old TS mod: TS was reading an already-resident in-memory object, this is reading
        // and parsing the file fresh, 13 times, back-to-back. Real cost, not a free toggle.
        var loggedLocations = new HashSet<string>();

        foreach (var locationName in Leaves.Utils.Instance.getLocationNames())
        {
            var location = Leaves.Utils.Instance.findLocationByCodeName(locationName);
            if (location?.LooseLoot == null)
            {
                continue;
            }

            if (config.LogAtStartup)
            {
                int totalAdjusted = 0;
                var snapshot = location.LooseLoot.Value;
                if (snapshot?.Spawnpoints != null)
                {
                    foreach (var point in snapshot.Spawnpoints)
                    {
                        if (AdjustPoint(point))
                        {
                            totalAdjusted++;
                        }
                    }
                }
                Leaves.Utils.Instance.printColor($"[L.E.W.D.S.] Found {totalAdjusted} spawns below floor at {locationName}.");
                loggedLocations.Add(locationName);
            }

            LooseLoot? Transform(LooseLoot? looseLoot)
            {
                if (looseLoot?.Spawnpoints == null)
                {
                    return looseLoot;
                }

                int totalAdjusted = 0;
                foreach (var point in looseLoot.Spawnpoints)
                {
                    if (AdjustPoint(point))
                    {
                        totalAdjusted++;
                    }
                }

                // Only log the first time this location's loot actually gets read (raid load,
                // ragfair generation, etc.), not every time - a location can be re-read many times
                // over a session. Already logged above if LogAtStartup handled this location.
                if (loggedLocations.Add(locationName))
                {
                    Leaves.Utils.Instance.printColor($"[L.E.W.D.S.] Found {totalAdjusted} spawns below floor at {locationName}.");
                }

                return looseLoot;
            }

            location.LooseLoot.AddTransformer(Transform);
        }

        return Task.CompletedTask;
    }

    // Returns true if this spawnpoint had at least one item matching a configured category/item
    // floor, and raises its Probability to that floor.
    private bool AdjustPoint(Spawnpoint point)
    {
        var items = point.Template?.Items;
        if (items == null)
        {
            return false;
        }

        bool adjusted = false;
        double probability = 0;

        foreach (var item in items)
        {
            //If an entry has a parent, it's not the top node in the entry. So we skip it.
            if (!string.IsNullOrEmpty(item.ParentId))
            {
                continue;
            }
            //Check if the item even exists.
            if (!itemDB.TryGetValue(item.Template, out var template))
            {
                continue;
            }

            // != 0 preserves the original TS's truthy check on the configured value - a category
            // or item entry explicitly set to 0 is treated as "no floor" (falls through to the
            // other one), same as elsewhere in this project's ports.
            bool hasCategory = config.Categories.TryGetValue(template.Parent.ToString(), out var categoryFloor) && categoryFloor != 0;
            bool hasItem = config.Items.TryGetValue(item.Template.ToString(), out var itemFloor) && itemFloor != 0;

            if (hasCategory || hasItem)
            {
                adjusted = true;
                double newProb = hasCategory ? categoryFloor : itemFloor;
                if (newProb > probability)
                {
                    probability = newProb;
                }
            }
        }

        if (adjusted && probability > (point.Probability ?? 0))
        {
            point.Probability = probability;
        }
        return adjusted;
    }
}
