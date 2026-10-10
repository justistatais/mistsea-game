using System.Collections.Immutable;
using Mist.Core.Campaign;
using Mist.Core.ChapterTwo;

namespace Mist.Core.Assets;

// The frozen campaign's complete original paragraph remains the source. No rewriting or timing inference.
public static class CampaignSpeechProjection
{
    public static ImmutableArray<ChapterParagraph> Paragraphs(CampaignMaterial material)=>[new(material.Id+"-original",material.English,material.Chinese)];
    public static WaveFacts Validate(CampaignMaterial material,RegionalAudioBinding binding,ReadOnlySpan<byte> audio)
    {
        if(material.Modality!="listening_script")throw new InvalidDataException("A written record is not a listening script.");
        return RegionalAudioRules.Validate(material.Id,material.Version,Paragraphs(material),binding,audio);
    }
}
