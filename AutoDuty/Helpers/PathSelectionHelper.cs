namespace AutoDuty.Helpers
{
    using System;
    using System.Collections.Generic;
    using Configurations;
    using Data;
    using ECommons.ExcelServices;

    public static class PathSelectionHelper
    {
        public static void AddPathSelectionEntry(uint territoryId)
        {
            if (!AutoDuty.Configuration.Meta.PathSelectionsByPath.ContainsKey(territoryId))
            {
                Dictionary<string, JobWithRole> jobs = [];
                AutoDuty.Configuration.Meta.PathSelectionsByPath.Add(territoryId, jobs);
                if (ContentPathsManager.DictionaryPaths.TryGetValue(territoryId, out ContentPathsManager.ContentPathContainer? container))
                    foreach (Job job in Enum.GetValues<Job>())
                    {
                        string path = container.SelectPath(out _, job)!.FileName;
                        jobs.TryAdd(path, JobWithRole.None);
                        jobs[path] |= job.JobToJobWithRole();
                    }

                ConfigurationProfileV2.Save();
            }
        }

        public static void RebuildDefaultPaths(uint territoryId)
        {
            ContentPathsManager.ContentPathContainer container = ContentPathsManager.DictionaryPaths[territoryId];

            Dictionary<string, JobWithRole>? pathJobConfigs = AutoDuty.Configuration.Meta.PathSelectionsByPath[territoryId];

            JobWithRole jwr = JobWithRole.All;

            if (pathJobConfigs != null)
            {
                foreach (string key in pathJobConfigs.Keys)
                    jwr &= ~pathJobConfigs[key];

                foreach (Job job in jwr.ContainedJobs())
                {
                    string path = container.SelectPath(out _, job)!.FileName;
                    pathJobConfigs.TryAdd(path, JobWithRole.None);
                    pathJobConfigs[path] |= job.JobToJobWithRole();
                }
            }

            ConfigurationProfileV2.Save();
        }
    }
}
