using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Carl.Dan
{
    public class MessagesDan : Dan, IFirehoseChatMessageListener, IFirehoseCommandListener, IHistoricAccumulatorInsertPreparer<string>
    {
        // Message Counts
        int m_messageAccPerSec = 0;
        int m_messageAccPerMin = 0;
        List<int> m_messagesPerSec = new List<int>();
        List<int> m_messagesPerMin = new List<int>();
        DateTime m_lastMinUpdate;

        HistoricAccumulator<string> m_wordHistory = new HistoricAccumulator<string>();
        Thread m_statsThread;

        public MessagesDan(IFirehose firehose)
            : base(firehose)
        {
            m_firehose.SubChatMessages(this);
            m_firehose.SubCommandListener(this);

            m_wordHistory.SetPreparer(this);

            m_statsThread = new Thread(StatsThread);
            m_statsThread.IsBackground = true;
            m_statsThread.Start();
        }

        public bool PrepareForInsert(ref string value)
        {
            // Remove empty strings.
            if (String.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            // Remove single letters and double letters.
            if(value.Length < 3)
            {
                return false;
            }
            // Lower the case.
            value = value.ToLower();
            return true;
        }

        public void OnChatMessage(ChatMessage msg)
        {
            // Update the counters
            m_messageAccPerSec++;
            m_messageAccPerMin++;

            // Add the words to the history.
            m_wordHistory.AddValue(msg.Text.Split(' '));
        }

        private void StatsThread()
        {
            while(true)
            {
                DateTime start = DateTime.Now;

                try
                {
                    // Update the averages.
                    CalcRollingAverage(m_messagesPerSec, m_messageAccPerSec);
                    m_messageAccPerSec = 0;

                    if ((DateTime.Now - m_lastMinUpdate).TotalSeconds > 60)
                    {
                        CalcRollingAverage(m_messagesPerMin, m_messageAccPerMin);
                        m_messageAccPerMin = 0;
                        m_lastMinUpdate = DateTime.Now;
                    }

                    Logger.Info(GetCurrentStatsOutput(5, true));

                    // Decay the history.
                    m_wordHistory.DecayHistory();
                }
                catch(Exception e)
                {
                    Logger.Error("Exception thrown in message dan stats thread.", e);
                }

                // Sleep for however long it takes.
                double sleepTime = 1000 - (DateTime.Now - start).TotalMilliseconds;
                if(sleepTime > 0)
                {
                    Thread.Sleep((int)sleepTime);
                }
            }
        }

        private string GetCurrentStatsOutput(int maxTopValues, bool includeCount = false)
        {
            // Update the averages.
            int avgPerSec = CalcRollingAverage(m_messagesPerSec);
            int avgPerMin = CalcRollingAverage(m_messagesPerMin);

            // Get the top words.
            var topWords = m_wordHistory.GetTopValues(maxTopValues);

            // Build the output
            string output = $"I'm processing {avgPerSec.ToString("n0", Carl.Culture)} msg/sec, {avgPerMin.ToString("n0", Carl.Culture)} msg/min. The top {maxTopValues} words on Mixer are currently: ";
            bool isFirst = true;
            foreach (var pair in topWords)
            {
                if(!isFirst)
                {
                    output += ", ";
                }
                isFirst = false;

                output += pair.Item1;
                if(includeCount)
                {
                    output += $"/{pair.Item2}";
                }
            }
            return output;
        }

        private int CalcRollingAverage(List<int> list, int valueToAdd = -1)
        {
            if(valueToAdd != -1)
            {
                list.Add(valueToAdd);
                if(list.Count > 5)
                {
                    list.RemoveAt(0);
                }
            }
            double accum = 0;
            foreach(int i in list)
            {
                accum += i;
            }
            return (int)(accum / (double)list.Count);
        }

        public async void OnCommand(string command, ChatMessage msg)
        {
            if(command.Equals("msgstats"))
            {
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, GetCurrentStatsOutput(10), CommandUtils.ShouldForceIsWhisper(msg));
            }
        }

    }
}
