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
    public class MessagesDan : Dan, IFirehoseChatMessageListener, IFirehoseCommandListener
    {
        // Message Counts
        int m_messageAccPerSec = 0;
        int m_messageAccPerMin = 0;
        List<int> m_messagesPerSec = new List<int>();
        List<int> m_messagesPerMin = new List<int>();
        DateTime m_lastMinUpdate;

        ConcurrentDictionary<string, int> m_wordDict = new ConcurrentDictionary<string, int>();
        ConcurrentDictionary<string, int> m_commonWordDict = new ConcurrentDictionary<string, int>();

        public MessagesDan(IFirehose firehose)
            : base(firehose)
        {
            m_firehose.SubChatMessages(this);
            m_firehose.SubCommandListener(this);

            // Build the common work list.
            SetupCommonWordsList();

            var _ignored = Task.Run(() => StatsThread());
        }

        public void OnChatMessage(ChatMessage msg)
        {
            // Update the counters
            m_messageAccPerSec++;
            m_messageAccPerMin++;

            string[] words = msg.Text.Split(' ');
            foreach(string word in words)
            {
                // Remove empty strings.
                if(String.IsNullOrWhiteSpace(word))
                {
                    continue;
                }

                // Lower case.
                string lowerWord = word.ToLower();

                // Filter out common words.
                int tmp;
                if(m_commonWordDict.TryGetValue(lowerWord, out tmp))
                {
                    continue;
                }

                // Add or update.
                int currentValue = 0;
                if(m_wordDict.TryGetValue(lowerWord, out currentValue))
                {
                    int newValue = currentValue + 1;
                    m_wordDict.TryUpdate(lowerWord, newValue, currentValue);
                }
                else
                {
                    m_wordDict.TryAdd(lowerWord, 1);
                }
            }
        }

        private async void StatsThread()
        {
            while(true)
            {
                DateTime start = DateTime.Now;

                try
                {
                    int avgPerSec = CalcRollingAverage(m_messagesPerSec, m_messageAccPerSec);
                    m_messageAccPerSec = 0;

                    int minValue = -1;
                    if ((DateTime.Now - m_lastMinUpdate).TotalMinutes > 60)
                    {
                        minValue = m_messageAccPerMin;
                        m_messageAccPerMin = 0;
                        m_lastMinUpdate = DateTime.Now;
                    }
                    int avgPerMin = CalcRollingAverage(m_messagesPerMin, minValue);

                    // Get the top words.
                    var topWords = GetTopWords(5);

                    DecayWordMap();

                    string output = $"Messages {avgPerSec}/s {avgPerMin}/m; top words: ";
                    foreach(var pair in topWords)
                    {
                        output += $"'{pair.Item1}'/{pair.Item2}, ";
                    }

                    Logger.Info(output);
                }
                catch(Exception e)
                {
                    Logger.Error("Exception thrown in message dan stats thread.", e);
                }

                // Sleep for however long it takes.
                double sleepTime = 1000 - (DateTime.Now - start).TotalMilliseconds;
                if(sleepTime > 0)
                {
                    await Task.Delay((int)sleepTime);
                }
            }
        }

        private void SetupCommonWordsList()
        {
            Logger.Info("Building common word list...");
            try
            {
                using (FileStream f = File.Open("CommonWords.txt", FileMode.Open))
                {
                    using (StreamReader reader = new StreamReader(f))
                    {
                        while(!reader.EndOfStream)
                        {
                            m_commonWordDict.TryAdd(reader.ReadLine(), 0);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error("Failed to open common words list.", e);
            }
        }

        private void DecayWordMap()
        {
            // Decay the word map.
            foreach (var pair in m_wordDict)
            {
                int newValue = pair.Value - 2;
                if (newValue <= 0)
                {
                    int tmp;
                    m_wordDict.TryRemove(pair.Key, out tmp);
                }
                else
                {
                    m_wordDict[pair.Key] = newValue;
                }
            }
        }

        private List<Tuple<string, int>> GetTopWords(int listCount)
        {
            // Build the top words
            List<Tuple<string, int>> topWords = new List<Tuple<string, int>>();

            foreach (var pair in m_wordDict)
            {
                int listMax = Math.Min(listCount, topWords.Count);
                bool shouldBeAdded = false;
                int pos;
                for (pos = listMax - 1; pos >= 0; pos--)
                {
                    if(pair.Value <= topWords[pos].Item2)
                    {
                        break;                    
                    }
                    shouldBeAdded = true;              
                }
                if(shouldBeAdded)
                {
                    pos++;
                    topWords.Insert(pos, new Tuple<string, int>(pair.Key, pair.Value));
                    if (topWords.Count > listCount)
                    {
                        topWords.RemoveAt(topWords.Count - 1);
                    }                    
                }
                if(topWords.Count < listCount)
                {
                    topWords.Add(new Tuple<string, int>(pair.Key, pair.Value));
                }
            }

            return topWords;
        }

        private int CalcRollingAverage(List<int> list, int valueToAdd = -1)
        {
            if(valueToAdd != -1)
            {
                list.Add(valueToAdd);
                if(list.Count > 10)
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
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, "", msg.IsWhisper);
            }
        }
    }
}
