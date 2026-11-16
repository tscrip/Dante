using Dante.Core.Config;

namespace Dante.Core.Behavior;

public sealed record QuipContext(WidgetDefinition App, double? OldValue, double? NewValue, int OldLevel, int NewLevel, string? Text);

/// <summary>
/// The "funny way" values get delivered: when something changes the PC picks a line that matches the
/// DANTE's personality and sends it along with the value, and DANTE performs it.
/// </summary>
public static class QuipBank
{
    private static readonly Dictionary<Personality, (string[] Up, string[] Down, string[] Worse, string[] Better)> Packs = new()
    {
        [Personality.Cheerful] = (
            Up: ["{name} went up to {value}{unit}!", "Ooh, {name}: {value}{unit}. Fresh delivery!", "+{delta} on {name}. We got this!", "Ding! {name} is now {value}{unit}"],
            Down: ["{name} dropped to {value}{unit}. Nice work!", "-{delta} {name}! High five!", "Look at you go - {name} {value}{unit}"],
            Worse: ["Uh-oh, {name} needs some love ({value}{unit})", "{name} is getting spicy: {value}{unit}"],
            Better: ["Phew! {name} is chill again", "{name} back to green. Snack time?"]),
        [Personality.Sarcastic] = (
            Up: ["Oh good, {name} is {value}{unit}. Just what we needed.", "{name}: {value}{unit}. They multiply when you look away.", "+{delta} {name}. I'll just wait here. Forever.", "Breaking: {name} now {value}{unit}. Shocking."],
            Down: ["{name} down to {value}{unit}. Who are you and what did you do with my human?", "-{delta} {name}. Don't let it go to your head.", "Wow, productivity. {name} {value}{unit}"],
            Worse: ["{name} at {value}{unit}. Totally fine. Everything is fine.", "Not to nag, but {name}: {value}{unit}"],
            Better: ["{name} is fine now. You may resume ignoring me.", "Crisis averted. Barely."]),
        [Personality.Dramatic] = (
            Up: ["NOOO! {name} rose to {value}{unit}!", "The prophecy! {name} is {value}{unit}!", "+{delta} {name}! Alert the council!", "{name}: {value}{unit}. We're doomed. DOOMED."],
            Down: ["A miracle! {name} fell to {value}{unit}!", "-{delta} {name}! Sing the songs of victory!", "Hero! {name} is down to {value}{unit}!"],
            Worse: ["{name} at {value}{unit}! The end is nigh!", "Red alert! {name}! {value}{unit}!"],
            Better: ["{name} is saved! Rejoice!", "The storm has passed. {name} is calm."]),
    };

    /// <summary>Returns a quip, or null if this change isn't worth talking about.</summary>
    public static string? Pick(Personality personality, QuipContext ctx, Random rng)
    {
        var app = ctx.App;
        if (!app.QuipsEnabled || ctx.OldValue is not { } oldV || ctx.NewValue is not { } newV) return null;

        var delta = newV - oldV;
        var levelChanged = ctx.NewLevel != ctx.OldLevel;
        if (!levelChanged && Math.Abs(delta) < Math.Max(app.QuipMinDelta, 1e-9)) return null;

        var pack = Packs[personality];
        string[] pool;
        if (levelChanged)
            pool = ctx.NewLevel > ctx.OldLevel ? pack.Worse : pack.Better;
        else if (delta > 0)
            pool = app.IncreaseQuips.Count > 0 ? [.. app.IncreaseQuips] : pack.Up;
        else
            pool = app.DecreaseQuips.Count > 0 ? [.. app.DecreaseQuips] : pack.Down;

        var tokens = new Dictionary<string, string>
        {
            ["name"] = app.Name,
            ["value"] = Format.Number(newV),
            ["delta"] = Format.Number(Math.Abs(delta)),
            ["unit"] = app.Unit,
            ["count"] = Format.Number(newV),
            ["text"] = ctx.Text ?? "",
        };
        return Templates.Render(pool[rng.Next(pool.Length)], tokens);
    }

    public static string Greeting(Personality p, Random rng) => p switch
    {
        Personality.Sarcastic => Pick(["Oh. You're back.", "Connected. Try to keep up.", "Hello again, human."], rng),
        Personality.Dramatic => Pick(["I LIVE! I'm connected!", "Reunited at last!", "The link is forged!"], rng),
        _ => Pick(["Hi friend! I'm connected!", "Hello hello! Ready to help!", "DANTE is online!"], rng),
    };

    private static string Pick(string[] options, Random rng) => options[rng.Next(options.Length)];
}
