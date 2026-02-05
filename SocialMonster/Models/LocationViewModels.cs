using System;
using System.Collections.Generic;

namespace SocialMonster.Models
{
    // Үндсэн Dashboard-ийн ViewModel
    public class LocationDashboardViewModel
    {
        public int TotalPosts { get; set; }
        public int TotalPostsWLoc { get; set; }
        public int UniqueLocationsCount { get; set; }
        public List<TopLocationStat> TopLocations { get; set; }
    }

    public class TopLocationStat
    {
        public string Name { get; set; }
        public int Count { get; set; }
        public decimal Percentage { get; set; }
    }

    // Жагсаалтын өгөгдөл (DataTable-д зориулсан)
    public class LocationListViewModel
    {
        public Guid PostID { get; set; }
        public string FromName { get; set; }
        public string PostContent { get; set; }
        public string UpdatedTime { get; set; }
        public string Url { get; set; }
        public string LocationName { get; set; } // Canonical Name
        public bool HasLocation { get; set; }
    }

    // Засах цонхны (Modal) ViewModel
    public class LocationEditViewModel
    {
        public Guid PostId { get; set; }
        public string FullContent { get; set; }
        public List<DetectedAliasViewModel> CurrentMappings { get; set; }
    }

    public class DetectedAliasViewModel
    {
        public Guid ContentPostID { get; set; } // Social.Content.Post-ийн ID
        public string CanonicalName { get; set; }
        public string AliasName { get; set; }
    }
}