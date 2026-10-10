public partial class CampaignClient
{
    public async Task ShowPerformanceText(string text)
    {
        await OpenWriter(Mist.Application.Campaign.CampaignService.RescueDraft);_editor!.Text=text;_activeDraft!.Edit(text);if(!await Save())throw new InvalidOperationException("Native performance editor was not saved.");
    }
}
