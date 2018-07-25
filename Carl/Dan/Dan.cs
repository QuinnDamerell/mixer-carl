using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Carl.Dan
{
    public abstract class Dan
    {
        public IFirehose m_firehose;

        public Dan(IFirehose firehose)
        {
            m_firehose = firehose;
        }
    }
}
