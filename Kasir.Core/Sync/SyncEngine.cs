using System;
using Microsoft.Data.Sqlite;
using Kasir.Data;
using Kasir.Utils;

namespace Kasir.Sync
{
    public class SyncEngine
    {
        private readonly SqliteConnection _db;
        private readonly ISyncFileWriter _fileWriter;
        private readonly ISyncFileReader _fileReader;
        private readonly IClock _clock;

        public string LastPushResult { get; private set; }
        public string LastPullResult { get; private set; }
        public DateTime LastSyncTime { get; private set; }

        public SyncEngine(SqliteConnection db)
        {
            _db = db;
            _fileWriter = new SyncFileWriter();
            _fileReader = new SyncFileReader();
            _clock = new ClockImpl();
        }

        public SyncEngine(
            SqliteConnection db,
            ISyncFileWriter fileWriter,
            ISyncFileReader fileReader,
            IClock clock)
        {
            _db = db;
            _fileWriter = fileWriter;
            _fileReader = fileReader;
            _clock = clock;
        }

        public SyncResult RunOnce()
        {
            var result = new SyncResult();

            try
            {
                // Push local changes
                var pushService = new PushService(_db, _fileWriter, _clock);
                var pushResult = pushService.Push();
                result.PushSuccess = pushResult.Success;
                result.PushEventCount = pushResult.EventCount;
                LastPushResult = pushResult.Success
                    ? string.Format("Pushed {0} events", pushResult.EventCount)
                    : "Push failed: " + pushResult.Error;
            }
            catch (Exception ex)
            {
                result.PushSuccess = false;
                LastPushResult = "Push error: " + ex.Message;
            }

            try
            {
                // Pull remote changes
                var pullService = new PullService(_db, _fileReader);
                var pullResult = pullService.Pull();
                result.PullSuccess = pullResult.Success;
                result.PullAppliedCount = pullResult.AppliedCount;
                LastPullResult = pullResult.Success
                    ? string.Format("Applied {0} events", pullResult.AppliedCount)
                    : "Pull issue: " + pullResult.Error;
            }
            catch (Exception ex)
            {
                result.PullSuccess = false;
                LastPullResult = "Pull error: " + ex.Message;
            }

            LastSyncTime = _clock.Now;
            result.Success = result.PushSuccess && result.PullSuccess;

            return result;
        }
    }

    public class SyncResult
    {
        public bool Success { get; set; }
        public bool PushSuccess { get; set; }
        public int PushEventCount { get; set; }
        public bool PullSuccess { get; set; }
        public int PullAppliedCount { get; set; }
    }
}
