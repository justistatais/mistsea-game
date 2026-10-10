using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Text;

namespace Mist.Application.Learning;

public sealed record LocalAdvice(string Message,int StartLine,int StartColumn,int EndLine,int EndColumn);
public sealed record WritingStructureView(int WordCount,ImmutableArray<LocalAdvice> Advice);
public static class LocalWritingFeedback
{
    // Observable structure only. No correction is inserted and no language score is inferred.
    public static WritingStructureView Inspect(string text)
    {
        var advice=ImmutableArray.CreateBuilder<LocalAdvice>();var lines=text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Split('\n');
        int words=Regex.Matches(text,"[A-Za-z]+(?:['’-][A-Za-z]+)*").Count;
        for(int line=0;line<lines.Length&&advice.Count<3;line++)
        {
            string value=lines[line];
            foreach(Match sentence in Regex.Matches(value,"[^.!?]+(?:[.!?]|$)"))
            {
                if(Regex.Matches(sentence.Value,"[A-Za-z]+(?:['’-][A-Za-z]+)*").Count<=40)continue;
                int start=value[..sentence.Index].EnumerateRunes().Count(),end=value[..(sentence.Index+sentence.Length)].EnumerateRunes().Count();
                advice.Add(new("这段句子较长，可检查条件和动作是否需要分开。点击定位原文。",line,start,line,end));if(advice.Count==3)break;
            }
        }
        return new(words,advice.ToImmutable());
    }
}
