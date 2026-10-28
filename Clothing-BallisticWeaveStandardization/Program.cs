using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis;
using Noggog;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await SynthesisPipeline.Instance
            .AddPatch<IFallout4Mod, IFallout4ModGetter>(RunPatch)
            .SetTypicalOpen(GameRelease.Fallout4, "Clothing Slots - Ballistic Weave Nerf.esp")
            .Run(args);
    }

    // Builds a BOD2 flag value from raw vanilla slot numbers (bit = slot - 30)
    private static BipedObjectFlag SlotMask(params int[] slotNumbers)
    {
        uint mask = 0;
        foreach (var slot in slotNumbers)
            mask |= (uint)1 << (slot - 30);
        return (BipedObjectFlag)mask;
    }

    private static bool IsClothing(IArmorGetter armor) => armor.ArmorRating == 0;

    public static void RunPatch(IPatcherState<IFallout4Mod, IFallout4ModGetter> state)
    {
        // [A]Torso(41), [A]L Arm(42), [A]R Arm(43), [A]L Leg(44), [A]R Leg(45)
        var armorSlots = SlotMask(41, 42, 43, 44, 45);
        // Hair Top(30), Hair Long(31), Headband(46)
        var headSlots = SlotMask(30, 31, 46);

        const string WeaveAttachPointEdid = "ap_Railroad_ClothingArmor";
        var weaveKeywords = new HashSet<FormKey>();

        foreach (var omod in state.LoadOrder.PriorityOrder.AObjectModification().WinningOverrides())
        {
            if (omod.AttachPoint.IsNull) continue;
            if (!omod.AttachPoint.TryResolve(state.LinkCache, out var kw)) continue;
            if (!string.Equals(kw.EditorID?.Trim(), WeaveAttachPointEdid, StringComparison.OrdinalIgnoreCase)) continue;

            if (weaveKeywords.Add(omod.AttachPoint.FormKey))
                Console.WriteLine($"Ballistic Weave attach point: {kw.EditorID} [{omod.AttachPoint.FormKey}] (from {omod.EditorID})");
        }

        if (weaveKeywords.Count == 0)
        {
            Console.WriteLine("Could not find the Ballistic Weave attach point keyword. Nothing to do.");
            return;
        }

        var weaveKey = weaveKeywords.First();

        int added = 0, removedHead = 0, removedCanEquip = 0;
        var correctWithWeave = new List<string>();
        var correctWithoutWeave = new List<string>();

        foreach (var armorGetter in state.LoadOrder.PriorityOrder.Armor().WinningOverrides())
        {
            if (armorGetter.MajorFlags.HasFlag(Armor.MajorFlag.NonPlayable))
                continue;

            var bod = armorGetter.BipedBodyTemplate;
            if (bod == null)
                continue;

            bool hasWeave = armorGetter.AttachParentSlots?.Any(k => weaveKeywords.Contains(k.FormKey)) ?? false;

            if (!hasWeave && !IsClothing(armorGetter))
                continue;

            var flags = bod.FirstPersonFlags;
            bool isHeadPiece = (flags & headSlots) != 0;
            bool occupiesArmorSlots = (flags & armorSlots) == armorSlots;

            bool wantWeave = !isHeadPiece && occupiesArmorSlots;

            var name = armorGetter.Name?.String ?? armorGetter.EditorID ?? armorGetter.FormKey.ToString();

            if (wantWeave == hasWeave)
            {
                var line = $"{name} ({armorGetter.EditorID}) [{armorGetter.FormKey}]";
                if (hasWeave)
                    correctWithWeave.Add($"{line} - occupies armor slots");
                else
                    correctWithoutWeave.Add($"{line} - {(isHeadPiece ? "head piece" : "can equip armor")}");
                continue;
            }

            var armor = state.PatchMod.Armors.GetOrAddAsOverride(armorGetter);

            if (wantWeave)
            {
                armor.AttachParentSlots ??= new();
                armor.AttachParentSlots.Add(weaveKey.ToLink<IKeywordGetter>());
                added++;
                Console.WriteLine($"+ weave: {name} ({armorGetter.EditorID}) [{armorGetter.FormKey}]");
            }
            else
            {
                armor.AttachParentSlots?.RemoveAll(k => weaveKeywords.Contains(k.FormKey));
                if (isHeadPiece) removedHead++; else removedCanEquip++;
                Console.WriteLine($"- weave: {name} ({armorGetter.EditorID}) [{armorGetter.FormKey}] ({(isHeadPiece ? "head piece" : "can equip armor")})");
            }
        }

        // Console.WriteLine();
        // Console.WriteLine($"Weave added: {added} | removed (head piece): {removedHead} | removed (can equip armor): {removedCanEquip}");

        PrintGroup("Already correct - has weave", correctWithWeave);
        PrintGroup("Already correct - no weave", correctWithoutWeave);
    }

    private static void PrintGroup(string title, List<string> items)
    {
        // Console.WriteLine();
        // Console.WriteLine($"===== {title} ({items.Count}) =====");
        // foreach (var s in items.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) Console.WriteLine(s);
    }
}