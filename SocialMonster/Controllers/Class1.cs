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
    public class Location : Controller
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


        // 1. Statistic
        [HttpGet]
        public JsonResult GetLocationStatistics(string category, string search, Guid? provinceId,
            Guid? districtId, DateTime? startDate, DateTime? endDate)
        {
            var totalPosts = db.Facebook_Posts
                .Select(x => x.ID)
                .Distinct()
                .Count();

            var query = db.Social_Content_Post.AsQueryable().Where(x => x.ContentID != null);

            if (!string.IsNullOrWhiteSpace(search) && category == "account")
                query = query.Where(cp =>
                    cp.Facebook_Posts.FromName.Contains(search));


            // ===============================
            // 2. LOCATION-BASED FILTERS
            // ===============================
            if (!string.IsNullOrWhiteSpace(search) && category == "location")
            {
                query = query.Where(p =>
                    db.Facebook_Posts.Any(cp =>
                        p.PostID == cp.ID &&
                        p.ContentID != null &&
                        p.Social_Contents.Text.Contains(search)
                    ));
            }

            if (startDate.HasValue) query = query.Where(x => x.Facebook_Posts.UpdateDate >= startDate);
            if (endDate.HasValue) query = query.Where(x => x.Facebook_Posts.UpdateDate <= endDate);


            if (districtId.HasValue)
            {
                // 1️⃣ Level 3,4,5-ын ID-г олж авна
                var districtRelatedIds = db.Social_Contents
                    .Where(c =>
                        c.ID == districtId.Value ||                 // Level 3
                        c.ParentID == districtId.Value ||           // Level 4
                        db.Social_Contents.Any(p =>
                            p.ParentID == districtId.Value &&
                            c.ParentID == p.ID                      // Level 5
                        )
                    )
                    .Where(c => c.Level != 6) // alias 
                    .Select(c => c.ID);

                // 2️⃣ Social_Content_Post filter
                query = query.Where(cp => districtRelatedIds.Contains(cp.ContentID.Value));
            }

            // 🔹 PROVINCE FILTER (Level 2)
            else if (provinceId.HasValue)
            {
                var level3Ids = db.Social_Contents
                    .Where(x => x.ParentID == provinceId.Value && x.Level == 3)
                    .Select(x => x.ID);

                var level4Ids = db.Social_Contents
                    .Where(x => level3Ids.Contains(x.ParentID.Value) && x.Level == 4)
                    .Select(x => x.ID);

                var level5Ids = db.Social_Contents
                    .Where(x => level4Ids.Contains(x.ParentID.Value) && x.Level == 5)
                    .Select(x => x.ID);

                var allIds = db.Social_Contents
                    .Where(x =>
                        x.ID == provinceId.Value ||
                        level3Ids.Contains(x.ID) ||
                        level4Ids.Contains(x.ID) ||
                        level5Ids.Contains(x.ID))
                    .Select(x => x.ID);

                query = query.Where(cp => allIds.Contains(cp.ContentID.Value));
            }





            var stats = query
                .GroupBy(x => x.Social_Contents.Text)
                .Select(g => new
                {
                    Name = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToList();

            var totalLocationHits = stats.Sum(x => x.Count);

            var result = stats.Select(x => new
            {
                x.Name,
                x.Count,
                Percentage = totalLocationHits == 0
                    ? 0
                    : Math.Round((decimal)x.Count * 100 / totalLocationHits, 2)
            });

            return Json(new
            {
                totalPosts,
                totalLocationHits,
                stats = result
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult GetLocationList(
            string category,
            string search,
            string location,
            int draw,
            int start,
            int length,
            DateTime? startDate,
            DateTime? endDate,
            bool? onlyWithLocation = true
        )
        {
            // ===============================
            // 1. BASE QUERY (POSTS)
            // ===============================
            var postQuery = db.Facebook_Posts
                .AsNoTracking()
                .AsQueryable();

            if (startDate.HasValue)
                postQuery = postQuery.Where(p => p.UpdateDate >= startDate);

            if (endDate.HasValue)
                postQuery = postQuery.Where(p => p.UpdateDate <= endDate);

            if (!string.IsNullOrWhiteSpace(search) && category == "account")
                postQuery = postQuery.Where(p => p.FromName.Contains(search));

            // ===============================
            // 2. LOCATION-BASED FILTERS
            // ===============================
            if (!string.IsNullOrWhiteSpace(search) && category == "location")
            {
                postQuery = postQuery.Where(p =>
                    db.Social_Content_Post.Any(cp =>
                        cp.PostID == p.ID &&
                        cp.ContentID != null &&
                        cp.Social_Contents.Text.Contains(search)
                    ));
            }

            if (!string.IsNullOrWhiteSpace(location))
            {
                postQuery = postQuery.Where(p =>
                    db.Social_Content_Post.Any(cp =>
                        cp.PostID == p.ID &&
                        cp.ContentID != null &&
                        cp.Social_Contents.Text == location
                    ));
            }

            if (onlyWithLocation == true)
            {
                postQuery = postQuery.Where(p =>
                    db.Social_Content_Post.Any(cp =>
                        cp.PostID == p.ID && cp.ContentID != null
                    ));
            }
            else if (onlyWithLocation == false)
            {
                postQuery = postQuery.Where(p =>
                    !db.Social_Content_Post.Any(cp =>
                        cp.PostID == p.ID && cp.ContentID != null
                    ));
            }

            // ===============================
            // 3. COUNTS (AFTER FILTER)
            // ===============================
            var recordsFiltered = postQuery.Count();
            var recordsTotal = db.Facebook_Posts.Count();

            // ===============================
            // 4. PAGING (SQL LEVEL)
            // ===============================
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

            // ===============================
            // 5. FETCH LOCATIONS (SECOND QUERY)
            // ===============================
            var locations = db.Social_Content_Post
                .AsNoTracking()
                .Where(cp => postIds.Contains(cp.PostID) && cp.ContentID != null)
                .Select(cp => new
                {
                    cp.PostID,
                    LocationName = cp.Social_Contents.Text
                })
                .ToList();

            // ===============================
            // 6. MERGE RESULT
            // ===============================
            var result = posts.Select(p =>
            {
                var loc = locations.FirstOrDefault(l => l.PostID == p.ID);

                return new LocationListViewModel
                {
                    PostID = p.ID,
                    FromName = p.FromName,
                    PostContent = p.Message,
                    UpdatedTime = (p.UpdateDate ?? DateTime.Now)
                                    .ToString("yyyy-MM-dd HH:mm"),
                    Url = p.FromUrl,
                    LocationName = loc?.LocationName,
                    HasLocation = loc != null
                };
            }).ToList();

            // ===============================
            // 7. DATATABLES RESPONSE
            // ===============================
            return Json(new
            {
                draw,
                recordsTotal,
                recordsFiltered,
                data = result
            }, JsonRequestBehavior.AllowGet);
        }


        [HttpGet]
        public JsonResult GetLocationFilters()
        {
            var locations = db.Social_Content_Post
                .AsNoTracking()
                .Where(x => x.ContentID != null)
                .Select(x => x.Social_Contents.Text)
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
                .Select(cp => new DetectedAliasViewModel
                {
                    ContentPostID = cp.ID,
                    CanonicalName = cp.Social_Contents.Text,
                    AliasName = db.Social_Contents.FirstOrDefault(a => a.ID == cp.MatchContentID).Text
                }).ToList();

            return Json(new { content = post.Message, mappings = mappings }, JsonRequestBehavior.AllowGet);
        }

        //update/delete record 
        [HttpPost]
        public JsonResult RemoveLocationMapping(Guid contentPostId)
        {
            try
            {
                var record = db.Social_Content_Post.Find(contentPostId);
                if (record != null)
                {
                    // update to NUL
                    record.ContentID = null;
                    record.MatchContentID = null;
                    db.SaveChanges();
                    return Json(new { success = true });
                }
                return Json(new { success = false, message = "Record not found" });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }



        bool IsValidAlias(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            if (token.Length < 2) return false;
            if (Regex.IsMatch(token, @"^\d+$")) return false;
            return true;
        }

        // new alias or location 
        [HttpPost]
        public JsonResult AddLocationMapping(Guid postId, string token, Guid? canonicalId)
        {
            if (!IsValidAlias(token))
                return Json(new { success = false, message = "Invalid alias" });

            if (!canonicalId.HasValue)
            {
                // Шинэ canonical location
                var canonical = new Social_Contents
                {
                    ID = Guid.NewGuid(),
                    Text = token,
                    Level = 5,
                    Type = "location"
                };
                db.Social_Contents.Add(canonical);
                canonicalId = canonical.ID;
            }

            var alias = new Social_Contents
            {
                ID = Guid.NewGuid(),
                Text = token,
                ParentID = canonicalId,
                Level = 6,
                Type = "location"
            };

            db.Social_Contents.Add(alias);

            db.Social_Content_Post.Add(new Social_Content_Post
            {
                ID = Guid.NewGuid(),
                PostID = postId,
                ContentID = canonicalId,
                MatchContentID = alias.ID
            });

            db.SaveChanges();
            return Json(new { success = true });
        }

        [HttpGet]
        public JsonResult GetMapLocations(
            string category,
            string search,
            string location,
            Guid? provinceId,
            Guid? districtId,
            DateTime? startDate,
            DateTime? endDate
)
        {
            var query = db.Social_Content_Post
                .AsNoTracking()
                .Where(cp => cp.ContentID != null);

            if (!string.IsNullOrWhiteSpace(search) && category == "account")
                query = query.Where(cp =>
                    cp.Facebook_Posts.FromName.Contains(search));


            // ===============================
            // 2. LOCATION-BASED FILTERS
            // ===============================
            if (!string.IsNullOrWhiteSpace(search) && category == "location")
            {
                query = query.Where(p =>
                    db.Facebook_Posts.Any(cp =>
                        p.PostID == cp.ID &&
                        p.ContentID != null &&
                        p.Social_Contents.Text.Contains(search)
                    ));
            }

            if (!string.IsNullOrWhiteSpace(location))
                query = query.Where(cp => cp.Social_Contents.Text == location);

            if (startDate.HasValue)
                query = query.Where(cp => cp.Facebook_Posts.UpdateDate >= startDate);

            if (endDate.HasValue)
                query = query.Where(cp => cp.Facebook_Posts.UpdateDate <= endDate);

            // 🔹 DISTRICT FILTER (Level 3)
            if (districtId.HasValue)
            {
                // 1️⃣ Level 3,4,5-ын ID-г олж авна
                var districtRelatedIds = db.Social_Contents
                    .Where(c =>
                        c.ID == districtId.Value ||                 // Level 3
                        c.ParentID == districtId.Value ||           // Level 4
                        db.Social_Contents.Any(p =>
                            p.ParentID == districtId.Value &&
                            c.ParentID == p.ID                      // Level 5
                        )
                    )
                    .Where(c => c.Level != 6) // alias 
                    .Select(c => c.ID);

                // 2️⃣ Social_Content_Post filter
                query = query.Where(cp => districtRelatedIds.Contains(cp.ContentID.Value));
            }

            // 🔹 PROVINCE FILTER (Level 2)
            else if (provinceId.HasValue)
            {
                var level3Ids = db.Social_Contents
                    .Where(x => x.ParentID == provinceId.Value && x.Level == 3)
                    .Select(x => x.ID);

                var level4Ids = db.Social_Contents
                    .Where(x => level3Ids.Contains(x.ParentID.Value) && x.Level == 4)
                    .Select(x => x.ID);

                var level5Ids = db.Social_Contents
                    .Where(x => level4Ids.Contains(x.ParentID.Value) && x.Level == 5)
                    .Select(x => x.ID);

                var allIds = db.Social_Contents
                    .Where(x =>
                        x.ID == provinceId.Value ||
                        level3Ids.Contains(x.ID) ||
                        level4Ids.Contains(x.ID) ||
                        level5Ids.Contains(x.ID))
                    .Select(x => x.ID);

                query = query.Where(cp => allIds.Contains(cp.ContentID.Value));
            }



            var result = query
                .Where(cp => cp.ContentID != null &&
                    cp.Social_Contents.Social_Location_More.Any(
                    l => l.Latitude != null && l.Longitude != null))
                .GroupBy(cp => new
                {
                    cp.Social_Contents.ID,
                    cp.Social_Contents.Text,
                    Lat = cp.Social_Contents.Social_Location_More
                        .Select(x => x.Latitude)
                        .FirstOrDefault(),
                    Lon = cp.Social_Contents.Social_Location_More
                        .Select(x => x.Longitude)
                        .FirstOrDefault()
                })
                .Select(g => new
                {
                    LocationId = g.Key.ID,
                    Name = g.Key.Text,
                    Lat = g.Key.Lat,
                    Lon = g.Key.Lon,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .Take(100)
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





    }
}