using Godot;

public partial class Root
{
    private double _audioDevicePoll;
    private string? _audioDeviceSignature;
    private void PauseContentAudio(string reason)
    {
        _campaignClient?.PauseCampaignContentAudio();
        bool paused=false;
        if(_lessonAudio is{} lesson&&(lesson.Playing||lesson.StreamPaused))
        {
            _lessonAudio.StreamPaused=true;
            if(_activeAudioId is{} id)_audioPositions[id]=_lessonAudio.GetPlaybackPosition();
            paused=true;
        }
        if(_lookupAudio is{} lookup&&(lookup.Playing||lookup.StreamPaused)){_lookupAudio.StreamPaused=true;paused=true;}
        if(paused&&_initialized)_status.Text=reason+"；内容播放已暂停，位置保留。回到音轨后明确继续播放。";
    }
    private void PollAudioDevice(double delta)
    {
        _audioDevicePoll+=delta;if(_audioDevicePoll<.5)return;_audioDevicePoll=0;
        string signature=AudioServer.OutputDevice+"|"+string.Join('|',AudioServer.GetOutputDeviceList());
        if(_audioDeviceSignature is not null&&_audioDeviceSignature!=signature)PauseContentAudio("音频输出设备已改变");
        _audioDeviceSignature=signature;
    }
}
