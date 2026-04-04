using System.Collections.Generic;

namespace Kasir.Models
{
    public class JournalEntry
    {
        public string JournalNo { get; set; }
        public string DocDate { get; set; }
        public string Remark { get; set; }
        public string PeriodCode { get; set; }
        public int ChangedBy { get; set; }
        public List<JournalLine> Lines { get; set; }

        public JournalEntry()
        {
            Lines = new List<JournalLine>();
        }
    }

    public class JournalLine
    {
        public string AccountCode { get; set; }
        public string SubCode { get; set; }
        public string ProductCode { get; set; }
        public string Remark { get; set; }
        public long Debit { get; set; }
        public long Credit { get; set; }
        public long QtyIn { get; set; }
        public long QtyOut { get; set; }
    }
}
