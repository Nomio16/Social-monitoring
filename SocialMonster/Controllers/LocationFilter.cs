using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace SocialMonster.Controllers
{
    public class LocationFilter
    {
        public string Category { get; set; }
        public string Search { get; set; }
        public string Location { get; set; }
        public Guid? ProvinceId { get; set; }
        public Guid? DistrictId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool? OnlyWithLocation { get; set; }
    }

    public class SearchSuggestionDto
    {
        public string Type { get; set; }   // "location" | "alias" | "account"
        public string Text { get; set; }
        public Guid? CanonicalId { get; set; }
    }



}