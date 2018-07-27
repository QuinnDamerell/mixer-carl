using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Carl.Dan
{
    public interface IHistoricAccumulatorInsertPreparer<T>
    {
        // Prepares the value for insert. If false is returned, the item won't be inserted.
        bool PrepareForInsert(ref T value);
    }

    public class HistoricAccumulator<T>
    {
        // DecayValue = CurrentValue ^ DecayFactor.
        public double DecayFactor = 0.98;

        ConcurrentDictionary<T, int> m_dict = new ConcurrentDictionary<T, int>();
        ConcurrentDictionary<T, bool> m_commonDict = null;
        IHistoricAccumulatorInsertPreparer<T> m_insertPrep = null;

        public HistoricAccumulator(string commonWordsFilePath = "CommonWords.txt")
        {
            if (!String.IsNullOrWhiteSpace(commonWordsFilePath))
            {
                // Build the common work list.
                SetupCommonWordsList(commonWordsFilePath);
            }
        }

        public void SetPreparer(IHistoricAccumulatorInsertPreparer<T> p)
        {
            m_insertPrep = p;
        }

        // Adds a list of values to the history
        public void AddValue(T[] values)
        {
            foreach(T v in values)
            {
                AddValue(v);
            }
        }

        // Adds a single value to the history
        public void AddValue(T value)
        {
            if(m_insertPrep != null)
            {
                if(!m_insertPrep.PrepareForInsert(ref value))
                {
                    return;
                }                    
            }

            // Filter out common values
            if(m_commonDict != null)
            {
                bool tmp;
                if (m_commonDict.TryGetValue(value, out tmp))
                {
                    return;
                }
            }


            // Add or update the value.
            int attempts = 0;
            while (attempts < 3)
            {
                attempts++;

                int currentValue = 0;
                if (m_dict.TryGetValue(value, out currentValue))
                {
                    if(m_dict.TryUpdate(value, currentValue + 1, currentValue))
                    {
                        break;
                    }
                }
                else
                {
                    if(m_dict.TryAdd(value, 1))
                    {
                        break;
                    }
                }
            }
        }

        // Decays the values in the map by the specified number of generations.
        public void DecayHistory(int decayGenerations = 1)
        {
            // Decay the word map.
            foreach (var pair in m_dict)
            {
                int newValue = pair.Value;
                for (int gen = 0; gen < decayGenerations; gen++)
                {
                    newValue = (int)Math.Round((Math.Pow(pair.Value, DecayFactor))) - 1;
                }

                if (newValue <= 0)
                {
                    int tmp;
                    m_dict.TryRemove(pair.Key, out tmp);
                }
                else
                {
                    m_dict[pair.Key] = newValue;
                }
            }
        }

        // Returns the current top values in the history.
        public List<Tuple<T, int>> GetTopValues(int listCount)
        {
            // Build the top words
            List<Tuple<T, int>> topValues = new List<Tuple<T, int>>();

            // For each of the values we currently have...
            foreach (var pair in m_dict)
            {
                // Setup        
                int listMax = listCount < topValues.Count ? listCount : topValues.Count;
                bool shouldBeAdded = false;
                int pos;

                // Take the rank of the value and see if it's higher than the
                // lowest in our exported list.
                for (pos = listMax - 1; pos >= 0; pos--)
                {
                    if (pair.Value <= topValues[pos].Item2)
                    {
                        break;
                    }
                    shouldBeAdded = true;
                }

                // If the value should be added, do it now at the position from the loop.
                if (shouldBeAdded)
                {
                    pos++;
                    topValues.Insert(pos, new Tuple<T, int>(pair.Key, pair.Value));

                    // If the list is too long, drop the lowest rank value.
                    if (topValues.Count > listCount)
                    {
                        topValues.RemoveAt(topValues.Count - 1);
                    }
                }

                // If the returned list isn't long enough, add this to it.
                if (topValues.Count < listCount)
                {
                    topValues.Add(new Tuple<T, int>(pair.Key, pair.Value));
                }
            }

            return topValues;
        }

        private void SetupCommonWordsList(string commonWordsFilePath)
        {
            m_commonDict = new ConcurrentDictionary<T, bool>();
            try
            {
                using (FileStream f = File.Open(commonWordsFilePath, FileMode.Open))
                {
                    using (StreamReader reader = new StreamReader(f))
                    {
                        while (!reader.EndOfStream)
                        {
                            m_commonDict.TryAdd((T)Convert.ChangeType(reader.ReadLine(), typeof(T)), false);
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
