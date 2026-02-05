using SocialMonster.DAL;
using SocialMonster.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Mvc;

namespace SocialMonster.Controllers
{
    //[Authorize]
    public class LocationController : Controller
    {
        //private SumbeeEntities db = new SumbeeEntities();
        private SocialDataLocalEntities db = new SocialDataLocalEntities();


        public ActionResult Index()
        {
            return View();
        }

        public ActionResult Map()
        {
            return View();
        }
        //post filter
        private IQueryable<Facebook_Posts> ApplyPostFilters(LocationFilter f)
        {
            IQueryable<Facebook_Posts> query = db.Facebook_Posts.AsNoTracking();


            // 🔹 Account search
            /*if (!string.IsNullOrWhiteSpace(f.Search) && f.Category == "account")
            {
                var accountIds = ResolveAccountCanonicalIds(f.Search);

                query = query.Where(p =>
                    db.Social_Account_Post.Any(ap =>
                        ap.PostID == p.ID &&
                        accountIds.Contains(ap.AccountID)));
            }*/

            // ApplyLocationFilters
            if (!string.IsNullOrWhiteSpace(f.Search) && f.Category == "account")
            {
                query = query.Where(cp =>
                    cp.FromName.Contains(f.Search));
            }



            // 🔹 Date range
            if (f.StartDate.HasValue)
                query = query.Where(p => p.UpdateDate >= f.StartDate);

            if (f.EndDate.HasValue)
                query = query.Where(p => p.UpdateDate <= f.EndDate);

            // 🔹 Location-based search
            if (!string.IsNullOrWhiteSpace(f.Search) && f.Category == "location")
            {
                var canonicalIds = ResolveCanonicalIdsFromSearch(f.Search);

                query = query.Where(p =>
                    db.Social_Content_Post.Any(cp =>
                        cp.PostID == p.ID &&
                        cp.ContentID.HasValue &&
                        canonicalIds.Contains(cp.ContentID.Value)));
            }



            return query;
        }



        //location filter
        private IQueryable<Social_Content_Post> ApplyLocationFilters(LocationFilter f)
        {
            var query = db.Social_Content_Post
                .AsNoTracking()
                .Where(cp => cp.ContentID != null);

            // 🔹 Account search
            if (!string.IsNullOrWhiteSpace(f.Search) && f.Category == "account")
            {
                query = query.Where(cp =>
                    cp.Facebook_Posts.FromName.Contains(f.Search));
            }

            // 🔹 Location search
            if (!string.IsNullOrWhiteSpace(f.Search) && f.Category == "location")
            {
                var canonicalIds = ResolveCanonicalIdsFromSearch(f.Search);

                query = query.Where(cp =>
                    cp.ContentID.HasValue &&
                    canonicalIds.Contains(cp.ContentID.Value));
            }


            // 🔹 Date range
            if (f.StartDate.HasValue)
                query = query.Where(cp => cp.Facebook_Posts.UpdateDate >= f.StartDate);

            if (f.EndDate.HasValue)
                query = query.Where(cp => cp.Facebook_Posts.UpdateDate <= f.EndDate);

            // 🔹 Location dropdown
            if (!string.IsNullOrWhiteSpace(f.Location))
                query = query.Where(cp => cp.Social_Contents.Text == f.Location);

            // 🔹 District / Province
            if (f.DistrictId.HasValue || f.ProvinceId.HasValue)
            {
                var validIds = ResolveLocationTreeIds(f.ProvinceId, f.DistrictId);
                query = query.Where(cp => validIds.Contains(cp.ContentID.Value));
            }

            return query;
        }

        private IQueryable<Guid> ResolveLocationTreeIds(Guid? provinceId, Guid? districtId)
        {
            if (districtId.HasValue)
            {
                return db.Social_Contents
                    .Where(c =>
                        c.ID == districtId ||
                        c.ParentID == districtId ||
                        db.Social_Contents.Any(p => p.ParentID == districtId && c.ParentID == p.ID)
                    )
                    .Where(c => c.Level != 6)
                    .Select(c => c.ID);
            }

            if (provinceId.HasValue)
            {
                var l3 = db.Social_Contents.Where(x => x.ParentID == provinceId && x.Level == 3);
                var l4 = db.Social_Contents.Where(x => l3.Select(a => a.ID).Contains(x.ParentID.Value));
                var l5 = db.Social_Contents.Where(x => l4.Select(a => a.ID).Contains(x.ParentID.Value));

                return db.Social_Contents
                    .Where(x =>
                        x.ID == provinceId ||
                        l3.Select(a => a.ID).Contains(x.ID) ||
                        l4.Select(a => a.ID).Contains(x.ID) ||
                        l5.Select(a => a.ID).Contains(x.ID))
                    .Select(x => x.ID);
            }

            return Enumerable.Empty<Guid>().AsQueryable();
        }



        // 1. Statistic
        [HttpGet]
        public JsonResult GetLocationStatistics(LocationFilter filter)
        {
            var postQuery = ApplyPostFilters(filter);
            var total = postQuery.Select(p => p.ID).Distinct().Count();

            var query = ApplyLocationFilters(filter);
            

            var stats = query
                .GroupBy(x => x.Social_Contents.Text)
                .Select(g => new
                {
                    Name = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToList();

            var totalHits = stats.Sum(x => x.Count);

            return Json(new
            {
                totalPosts = total,
                totalPostsWLoc = query.Select(x => x.PostID).Distinct().Count(),
                totalLocationHits = totalHits,
                stats = stats.Select(x => new
                {
                    x.Name,
                    x.Count,
                    Percentage = totalHits == 0 ? 0 : (decimal)x.Count * 100 / totalHits
                })
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult GetLocationList(LocationFilter filter, int draw, int start, int length)
        {
            // 1️⃣ Base post filters (account, date, search)
            var postQuery = ApplyPostFilters(filter);

            // 2️⃣ Location-based filtering (province / district / dropdown)
            IQueryable<Guid> postIdsFromLocation = null;

            if (!string.IsNullOrWhiteSpace(filter.Location)
                || filter.ProvinceId.HasValue
                || filter.DistrictId.HasValue)
            {
                var locationQuery = ApplyLocationFilters(filter);

                postIdsFromLocation = locationQuery
                    .Select(cp => cp.PostID)
                    .Distinct();
            }

            if (postIdsFromLocation != null)
            {
                postQuery = postQuery.Where(p => postIdsFromLocation.Contains(p.ID));
            }

            // 3️⃣ OnlyWithLocation (LIST only)
            if (filter.OnlyWithLocation == true)
            {
                postQuery = postQuery.Where(p =>
                    db.Social_Content_Post.Any(cp =>
                        cp.PostID == p.ID &&
                        cp.ContentID != null));
            }

            var recordsTotal = postQuery.Count();

            // 4️⃣ Paging
            var posts = postQuery
                .OrderByDescending(p => p.UpdateDate)
                .Skip(start)
                .Take(length)
                .Select(p => new
                {
                    p.ID,
                    p.FromName,
                    p.Message,
                    p.UpdateDate,
                    p.FromUrl
                })
                .ToList();

            var postIds = posts.Select(p => p.ID).ToList();

            // 5️⃣ Load location names in ONE query
            var locations = db.Social_Content_Post
                .AsNoTracking()
                .Where(cp =>
                    postIds.Contains(cp.PostID) &&
                    cp.ContentID != null)
                .GroupBy(cp => cp.PostID)
                .Select(g => new
                {
                    PostID = g.Key,
                    LocationName = g.Select(x => x.Social_Contents.Text).FirstOrDefault()
                })
                .ToList();

            var data = posts.Select(p => new LocationListViewModel
            {
                PostID = p.ID,
                FromName = p.FromName,
                PostContent = p.Message,
                UpdatedTime = p.UpdateDate?.ToString("yyyy-MM-dd HH:mm"),
                Url = p.FromUrl,
                LocationName = locations.FirstOrDefault(l => l.PostID == p.ID)?.LocationName
            });

            return Json(new
            {
                draw,
                recordsTotal,
                recordsFiltered = recordsTotal,
                data
            }, JsonRequestBehavior.AllowGet);
        }




        //options of location dropdown in location list 
        [HttpGet]
        public JsonResult GetLocationFilters(LocationFilter filter)
        {
            var locations = ApplyLocationFilters(filter)
                .Select(cp => cp.Social_Contents.Text)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            return Json(locations, JsonRequestBehavior.AllowGet);
        }


        // edit
        public JsonResult GetEditDetails(Guid postId)
        {
            var post = db.Facebook_Posts.Find(postId);

            var mappings = db.Social_Content_Post
                .Where(cp => cp.PostID == postId && cp.ContentID != null)
                .Select(cp => new
                {
                    cp.ID, // ContentPostID
                    Alias = db.Social_Contents
                        .Where(a => a.ID == cp.MatchContentID)
                        .Select(a => a.Text)
                        .FirstOrDefault(),
                    Canonical = cp.Social_Contents.Text
                })
                .ToList();

            return Json(new
            {
                content = post.Message,
                mappings
            }, JsonRequestBehavior.AllowGet);
        }


        //delete record 
        [HttpPost]
        public JsonResult RemoveLocationMapping(Guid contentPostId)
        {
            var record = db.Social_Content_Post.Find(contentPostId);
            if (record == null)
                return Json(new { success = false });

            record.ContentID = null;
            record.MatchContentID = null;
            db.SaveChanges();

            return Json(new { success = true });
        }
        


        bool IsValidAlias(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            if (token.Length < 2) return false;
            if (token.Length > 50) return false;

            // Only numbers (too generic)
            if (Regex.IsMatch(token, @"^\d+$")) return false;

            // Common stop-words
            var banned = new[] { "энд", "тэнд", "энэ", "тэр" };
            if (banned.Contains(token.ToLower())) return false;

            return true;
        }


        [HttpPost]
        public JsonResult AddLocationMapping(Guid postId, string token, Guid? canonicalId, string canonicalText)
        {
            if (!IsValidAlias(token))
                return Json(new { success = false, message = "Invalid alias" });

            // ================= CANONICAL =================
            Social_Contents canonical;

            if (canonicalId.HasValue)
            {
                canonical = db.Social_Contents.Find(canonicalId.Value);
                if (canonical == null)
                    return Json(new { success = false, message = "Canonical not found" });
            }
            else
            {
                // шинэ canonical
                canonical = new Social_Contents
                {
                    ID = Guid.NewGuid(),
                    Text = canonicalText,
                    Level = 5,
                    Type = "location"
                };
                db.Social_Contents.Add(canonical);
            }

            // ================= ALIAS =================
            var alias = db.Social_Contents.FirstOrDefault(x =>
                x.Level == 6 &&
                x.ParentID == canonical.ID &&
                x.Text == token);

            if (alias == null)
            {
                alias = new Social_Contents
                {
                    ID = Guid.NewGuid(),
                    Text = token,
                    ParentID = canonical.ID,
                    Level = 6,
                    Type = "location"
                };
                db.Social_Contents.Add(alias);
            }

            // ================= MAPPING =================
            bool alreadyMapped = db.Social_Content_Post.Any(x =>
                x.PostID == postId &&
                x.MatchContentID == alias.ID);

            if (!alreadyMapped)
            {
                db.Social_Content_Post.Add(new Social_Content_Post
                {
                    ID = Guid.NewGuid(),
                    PostID = postId,
                    ContentID = canonical.ID,
                    MatchContentID = alias.ID
                });
            }

            db.SaveChanges();

            return Json(new { success = true });
        }

        [HttpGet]
        public JsonResult GetMapLocations(LocationFilter filter)
        {
                // 1️⃣ Location hits (NO Distinct here)
                var locationHits = ApplyLocationFilters(filter)
                    .Select(cp => new
                    {
                        LocationId = cp.ContentID.Value,
                        LocationName = cp.Social_Contents.Text,
                        cp.PostID
                    });

                // 2️⃣ Aggregate by location
                var grouped = locationHits
                    .GroupBy(x => new { x.LocationId, x.LocationName })
                    .Select(g => new
                    {
                        LocationId = g.Key.LocationId,
                        Name = g.Key.LocationName,
                        Count = g.Select(x => x.PostID).Distinct().Count()
                    })
                    .ToList();

                var locationIds = grouped.Select(x => x.LocationId).ToList();

                var coords = db.Social_Location_More
                    .AsNoTracking()
                    .Where(l =>
                        locationIds.Contains(l.ContentID) &&
                        l.Latitude != null &&
                        l.Longitude != null)
                    .Select(l => new
                    {
                        l.ContentID,
                        l.Latitude,
                        l.Longitude
                    })
                    .ToList();

                var result = grouped
                    .Select(g =>
                    {
                        var c = coords.FirstOrDefault(x => x.ContentID == g.LocationId);
                        if (c == null) return null;

                        return new
                        {
                            LocationId = g.LocationId,
                            Name = g.Name,
                            Count = g.Count,
                            Lat = c.Latitude,
                            Lon = c.Longitude
                        };
                    })
                    .Where(x => x != null)
                    .OrderByDescending(x => x.Count)
                    .Take(200)
                    .ToList();

            return Json(result, JsonRequestBehavior.AllowGet);
        }



        [HttpGet]
        public JsonResult GetProvinces()
        {
            var data = db.Social_Contents
                .AsNoTracking()
                .Where(x => x.Level == 2)
                .OrderBy(x => x.Text)
                .Select(x => new {
                    x.ID,
                    x.Text
                })
                .ToList();

            return Json(data, JsonRequestBehavior.AllowGet);
        }


        [HttpGet]
        public JsonResult GetDistricts(Guid provinceId)
        {
            var data = db.Social_Contents
                .AsNoTracking()
                .Where(x => x.Level == 3 && x.ParentID == provinceId)
                .OrderBy(x => x.Text)
                .Select(x => new {
                    x.ID,
                    x.Text
                })
                .ToList();

            return Json(data, JsonRequestBehavior.AllowGet);
        }


        //Газрын зургийг тухайн бүс рүү zoom хийх
        [HttpGet]
        public JsonResult GetLocationBounds(Guid id)
        {
            var coords = db.Social_Content_Post
                .Where(cp =>
                    cp.ContentID == id ||
                    cp.Social_Contents.ParentID == id
                )
                .SelectMany(cp => cp.Social_Contents.Social_Location_More)
                .Where(l => l.Latitude != null && l.Longitude != null)
                .Select(l => new { l.Latitude, l.Longitude })
                .ToList();

            if (!coords.Any())
                return Json(null, JsonRequestBehavior.AllowGet);

            return Json(new
            {
                minLat = coords.Min(x => x.Latitude),
                maxLat = coords.Max(x => x.Latitude),
                minLon = coords.Min(x => x.Longitude),
                maxLon = coords.Max(x => x.Longitude)
            }, JsonRequestBehavior.AllowGet);
        }
        //Post date range
        [HttpGet]
        public JsonResult GetPostDateRange()
        {
            var dates = db.Facebook_Posts
                .Where(p => p.UpdateDate != null)
                .Select(p => p.UpdateDate.Value)
                .ToList();

            if (!dates.Any())
                return Json(null, JsonRequestBehavior.AllowGet);

            return Json(new
            {
                minDate = dates.Min().ToString("yyyy-MM-dd"),
                maxDate = dates.Max().ToString("yyyy-MM-dd")
            }, JsonRequestBehavior.AllowGet);
        }


        
        //dictionary search api
        [HttpGet]
        public JsonResult SearchAliasHierarchy(string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
                return Json(new List<object>(), JsonRequestBehavior.AllowGet);

            // 1️⃣ alias (level 6) search
            var aliases = db.Social_Contents
                .AsNoTracking()
                .Where(x =>
                    x.Level == 6 &&
                    x.Text.Contains(q))
                .Select(x => new
                {
                    AliasId = x.ID,
                    AliasText = x.Text,
                    CanonicalId = x.ParentID
                })
                .ToList();

            if (!aliases.Any())
                return Json(new { found = false }, JsonRequestBehavior.AllowGet);

            // 2️⃣ canonical + all aliases under it
            var canonicalIds = aliases.Select(x => x.CanonicalId).Distinct().ToList();

            var canonicals = db.Social_Contents
                .Where(x => canonicalIds.Contains(x.ID))
                .Select(c => new
                {
                    CanonicalId = c.ID,
                    CanonicalText = c.Text,
                    Aliases = db.Social_Contents
                        .Where(a => a.ParentID == c.ID && a.Level == 6)
                        .Select(a => new { a.ID, a.Text })
                        .ToList()
                })
                .ToList();

            return Json(new
            {
                found = true,
                canonicals
            }, JsonRequestBehavior.AllowGet);
        }

        private IQueryable<Guid> ResolveCanonicalIdsFromSearch(string search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return Enumerable.Empty<Guid>().AsQueryable();

            // 1️⃣ Alias match
            var canonicalFromAlias = db.Social_Contents
                .Where(x => x.Level == 6 && x.Text.Contains(search))
                .Select(x => x.ParentID.Value);

            // 2️⃣ Canonical direct match
            var canonicalDirect = db.Social_Contents
                .Where(x => x.Level == 5 && x.Text.Contains(search))
                .Select(x => x.ID);

            return canonicalFromAlias
                .Union(canonicalDirect)
                .Distinct();
        }
        /*
        private IQueryable<Guid> ResolveAccountCanonicalIds(string search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return Enumerable.Empty<Guid>().AsQueryable();

            var fromAlias = db.Social_Accounts
                .Where(a => a.Level == 2 && a.Text.Contains(search))
                .Select(a => a.ParentID.Value);

            var direct = db.Social_Accounts
                .Where(a => a.Level == 1 && a.Text.Contains(search))
                .Select(a => a.ID);

            return fromAlias.Union(direct).Distinct();
        }*/




    }
}