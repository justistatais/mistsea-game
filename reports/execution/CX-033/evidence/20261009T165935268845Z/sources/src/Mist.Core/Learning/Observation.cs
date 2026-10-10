namespace Mist.Core.Learning;

public enum InputModality { Reading, Listening, Writing, StructuredChoice, Mixed }
public sealed record SupportUse(string Kind, string MaterialId, string ParagraphId, int StartScalar, int EndScalar);
public sealed record LearningObservation(string Id, string FamilyId, string AttemptId, InputModality Modality,
    bool Submitted, bool PersistenceComplete, bool AudioAssetAvailable, bool SubtitlesViewed,
    IReadOnlyList<SupportUse> Support, string SourceVersion, string? SelectedEvidence,
    bool AudioPlaybackStarted=false,decimal PlaybackSpeed=1m,int Replays=0,int Pauses=0);
public sealed record LearningView(string State, bool IndependentListening, bool IndependentWriting, bool EligibleUnfamiliar, string[] Reasons);
public static class ObservationRules
{
    public static LearningView Project(LearningObservation observation, bool familyPreviouslySeen)
    {
        if (string.IsNullOrWhiteSpace(observation.FamilyId) || string.IsNullOrWhiteSpace(observation.AttemptId) || string.IsNullOrWhiteSpace(observation.SourceVersion)) throw new ArgumentException("Observation identity/source is required.");
        var reasons = new List<string>();
        if (!observation.Submitted) reasons.Add("preload_or_playback_is_not_comprehension");
        if (!observation.PersistenceComplete) reasons.Add("conditions_incomplete");
        if (observation.SubtitlesViewed) reasons.Add("subtitles_viewed");
        if (!observation.AudioAssetAvailable && observation.Modality == InputModality.Listening) reasons.Add("audio_missing");
        if (!observation.AudioPlaybackStarted && observation.Modality == InputModality.Listening) reasons.Add("audio_not_played");
        if (familyPreviouslySeen) reasons.Add("same_material_family");
        if (observation.Support.Count > 0) reasons.Add("support_used");
        bool usable = observation.Submitted && observation.PersistenceComplete;
        return new(!observation.PersistenceComplete ? "conditions_incomplete" : observation.Submitted ? "observed_without_band_estimate" : "not_evidence",
            usable && observation.Modality == InputModality.Listening && observation.AudioAssetAvailable && observation.AudioPlaybackStarted && !observation.SubtitlesViewed && observation.Support.Count == 0,
            usable && observation.Modality == InputModality.Writing && observation.Support.Count == 0,
            usable && !familyPreviouslySeen && observation.Modality != InputModality.StructuredChoice, reasons.ToArray());
    }
}
