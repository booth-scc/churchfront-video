namespace CueVideo.Core;

/// <summary>
/// One button in a camera's shot row: a shot saved in the setup file, or one of the
/// camera's own presets. Exactly one of <see cref="Shot"/> and <see cref="Preset"/> is set;
/// <see cref="Problem"/> says why a saved shot cannot act.
/// </summary>
public sealed record ShotButton
{
    private ShotButton(string label, PtzShot? shot, int? preset, string? problem)
    {
        Label = label;
        Shot = shot;
        Preset = preset;
        Problem = problem;
    }

    public string Label { get; }
    public PtzShot? Shot { get; }
    public int? Preset { get; }
    public string? Problem { get; }

    public static ShotButton FromShot(PtzShot shot) => new(shot.DisplayName, shot, null, shot.Problem);

    public static ShotButton FromPreset(int number, string name) => new(name, null, number, null);
}

/// <summary>
/// The buttons a camera's shot row offers, in order. Saved shots come from the setup file;
/// a camera with none falls back to the presets named there. Names never come from the
/// window, so a setup says "Stage" instead of "Full Stage".
/// </summary>
public static class CameraShotRow
{
    /// <summary>What a camera row says when it has neither saved shots nor named presets.</summary>
    public const string NoShots = "No shots";

    public static IReadOnlyList<ShotButton> For(PtzCameraConfig camera)
    {
        if (camera.Shots.Count > 0) return camera.Shots.Select(ShotButton.FromShot).ToList();

        return NamedPresets(camera)
            .Select(preset => ShotButton.FromPreset(preset.Number, preset.Name))
            .ToList();
    }

    /// <summary>Presets with a name, in the camera's preset order and then by number.</summary>
    private static IEnumerable<(int Number, string Name)> NamedPresets(PtzCameraConfig camera)
    {
        var place = new Dictionary<int, int>();
        for (var index = 0; index < camera.PresetOrder.Count; index++)
        {
            place.TryAdd(camera.PresetOrder[index], index);
        }

        var named = new List<(int Number, string Name, int Place)>();
        foreach (var preset in camera.PresetNames)
        {
            if (!int.TryParse(preset.Key, out var number) || string.IsNullOrWhiteSpace(preset.Value)) continue;

            named.Add((number, preset.Value, place.TryGetValue(number, out var index) ? index : int.MaxValue));
        }

        return named
            .OrderBy(preset => preset.Place)
            .ThenBy(preset => preset.Number)
            .Select(preset => (preset.Number, preset.Name));
    }
}
