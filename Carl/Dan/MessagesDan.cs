using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;

namespace Carl.Dan
{
    public class MessagesDan : Dan, IFirehoseChatMessageListener
    {
        Timer m_statsTimer;
        int m_messageCount = 0;
        ConcurrentDictionary<string, int> m_wordDict = new ConcurrentDictionary<string, int>();
        ConcurrentDictionary<string, int> m_commonWordDict = new ConcurrentDictionary<string, int>();

        public MessagesDan(IFirehose firehose)
            : base(firehose)
        {
            m_firehose.SubChatMessages(this);

            SetupCommonWordsList();

            m_statsTimer = new Timer();
            m_statsTimer.Elapsed += StatsTimerTick;
            m_statsTimer.Interval = 1000;
            m_statsTimer.Start();
        }

        public void OnChatMessage(ChatMessage msg)
        {
            m_messageCount++;
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

        private void StatsTimerTick(object sender, ElapsedEventArgs e)
        {
            string topWord = "[none]";
            int count = 0;
            foreach(var pair in m_wordDict)
            {
                if(pair.Value > count)
                {
                    topWord = pair.Key;
                    count = pair.Value;
                }
                m_wordDict[pair.Key] = Math.Max(0, pair.Value - 5);
            }

            Logger.Info($"Messages per second {m_messageCount}; top word '{topWord}' with {count}");
            m_messageCount = 0;
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
    }
}
