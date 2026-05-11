using System.Collections.Concurrent;
using GeoClubBot.MockGeoGuessr.DataStore;
using Microsoft.AspNetCore.Mvc;
using Quartz;
using Quartz.Impl.Matchers;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.MockGeoGuessr.Endpoints;

[ApiController]
[Route("mock/api")]
public class MockManagementController(MockGeoGuessrDataStore store, ISchedulerFactory schedulerFactory) : ControllerBase
{
    [HttpGet("state")]
    public IActionResult GetState()
    {
        var clubs = store.Clubs.Select(c =>
        {
            var memberCount = store.ClubMembers.TryGetValue(c.Key, out var m) ? m.Count : 0;
            return new { c.Value.ClubId, c.Value.Name, c.Value.Level, c.Value.Xp, MemberCount = memberCount, c.Value.MaxMemberCount, c.Value.Tag, c.Value.Description };
        });
        return Ok(new
        {
            clubs,
            users = store.Users.Values.Select(u => new { u.Id, u.Nick, u.CountryCode, u.IsProUser, u.Competitive.Elo, u.Competitive.Rating }),
            challengeCount = store.Challenges.Count
        });
    }

    [HttpGet("clubs/{clubId:guid}")]
    public IActionResult GetClub(Guid clubId)
    {
        if (!store.Clubs.TryGetValue(clubId, out var club))
            return NotFound();
        var members = store.ClubMembers.TryGetValue(clubId, out var m)
            ? m.Values.Select(mb => new { mb.User.UserId, mb.User.Nick, mb.Xp, mb.WeeklyXp, mb.Role, JoinedAt = mb.JoinedAt.ToString("yyyy-MM-dd") })
            : [];
        var activities = store.ClubActivities.TryGetValue(clubId, out var a)
            ? a.OrderByDescending(x => x.RecordedAt).Take(20).Select(x => new
            {
                x.UserId,
                UserNick = store.Users.TryGetValue(x.UserId, out var u) ? u.Nick : x.UserId,
                x.XpReward,
                RecordedAt = x.RecordedAt.ToString("yyyy-MM-dd HH:mm:ss")
            })
            : [];
        return Ok(new
        {
            club = new { club.ClubId, club.Name, club.Level, club.Xp, club.MaxMemberCount, club.Tag, club.Description, club.Language, club.JoinRule },
            members,
            activities
        });
    }

    [HttpPost("clubs/{clubId:guid}")]
    public IActionResult UpdateClub(Guid clubId, [FromBody] UpdateClubRequest req)
    {
        if (!store.Clubs.TryGetValue(clubId, out var club))
            return NotFound();
        if (req.Name is not null) club.Name = req.Name;
        if (req.Level.HasValue) club.Level = req.Level.Value;
        if (req.Xp.HasValue) club.Xp = req.Xp.Value;
        if (req.MaxMemberCount.HasValue) club.MaxMemberCount = req.MaxMemberCount.Value;
        if (req.Tag is not null) club.Tag = req.Tag;
        if (req.Description is not null) club.Description = req.Description;
        store.NotifyDataChanged();
        return Ok();
    }

    [HttpPost("clubs/{clubId:guid}/level")]
    public IActionResult IncrementLevel(Guid clubId)
    {
        if (!store.Clubs.TryGetValue(clubId, out var club))
            return NotFound();
        club.Level++;
        store.NotifyDataChanged();
        return Ok(new { club.Level });
    }

    [HttpPost("clubs/{clubId:guid}/members")]
    public IActionResult AddMember(Guid clubId, [FromBody] AddMemberRequest req)
    {
        if (!store.Users.TryGetValue(req.UserId, out var user))
            return NotFound("User not found");
        var members = store.ClubMembers.GetOrAdd(clubId, _ => new ConcurrentDictionary<string, ClubMemberDto>());
        members[user.Id] = new ClubMemberDto
        {
            User = new ClubMemberUserDto
            {
                UserId = user.Id, Nick = user.Nick, Avatar = "", FullBodyAvatar = "",
                BorderUrl = "", IsVerified = false, Flair = 0, CountryCode = user.CountryCode, TierId = 0, ClubUserType = 0
            },
            Role = 0, JoinedAt = DateTimeOffset.UtcNow, IsOnline = false, Xp = 0, WeeklyXp = 0, LastActive = null
        };
        store.NotifyDataChanged();
        return Ok();
    }

    [HttpDelete("clubs/{clubId:guid}/members/{userId}")]
    public IActionResult RemoveMember(Guid clubId, string userId)
    {
        if (store.ClubMembers.TryGetValue(clubId, out var members))
            members.TryRemove(userId, out _);
        store.NotifyDataChanged();
        return Ok();
    }

    [HttpPost("clubs/{clubId:guid}/members/{userId}/move")]
    public IActionResult MoveMember(Guid clubId, string userId, [FromBody] MoveMemberRequest req)
    {
        if (!store.ClubMembers.TryGetValue(clubId, out var source))
            return NotFound();
        if (!source.TryRemove(userId, out var member))
            return NotFound("Member not found");
        member.Xp = 0;
        member.WeeklyXp = 0;
        var target = store.ClubMembers.GetOrAdd(req.TargetClubId, _ => new ConcurrentDictionary<string, ClubMemberDto>());
        target[userId] = member;
        store.NotifyDataChanged();
        return Ok();
    }

    [HttpPost("clubs/{clubId:guid}/members/{userId}/xp")]
    public IActionResult AddXp(Guid clubId, string userId, [FromBody] AddXpRequest req)
    {
        if (!store.ClubMembers.TryGetValue(clubId, out var members))
            return NotFound();
        if (!members.TryGetValue(userId, out var member))
            return NotFound("Member not found");
        var amount = req.Amount > 0 ? req.Amount : 20;
        member.Xp += amount;
        store.AddActivity(clubId, userId, amount);
        store.NotifyDataChanged();
        return Ok(new { member.Xp });
    }

    [HttpPost("clubs/{clubId:guid}/members/{userId}")]
    public IActionResult UpdateMember(Guid clubId, string userId, [FromBody] UpdateMemberRequest req)
    {
        if (!store.ClubMembers.TryGetValue(clubId, out var members))
            return NotFound();
        if (!members.TryGetValue(userId, out var member))
            return NotFound("Member not found");
        if (req.Xp.HasValue) member.Xp = req.Xp.Value;
        if (req.WeeklyXp.HasValue) member.WeeklyXp = req.WeeklyXp.Value;
        if (req.Role.HasValue) member.Role = req.Role.Value;
        store.NotifyDataChanged();
        return Ok();
    }

    [HttpPost("clubs/{clubId:guid}/activities")]
    public IActionResult AddActivity(Guid clubId, [FromBody] AddActivityRequest req)
    {
        store.AddActivity(clubId, req.UserId, req.XpReward > 0 ? req.XpReward : 20);
        store.NotifyDataChanged();
        return Ok();
    }

    [HttpPost("users")]
    public IActionResult CreateUser([FromBody] CreateUserRequest req)
    {
        var userId = string.IsNullOrWhiteSpace(req.UserId) ? Guid.NewGuid().ToString("N")[..24] : req.UserId;
        var user = new UserDto
        {
            Id = userId, Nick = req.Nick.Trim(), Created = DateTimeOffset.UtcNow,
            IsProUser = req.IsProUser, Type = "user", IsVerified = false,
            CustomImage = "", FullBodyPin = "", BorderUrl = "", Color = 0,
            Url = $"/user/{userId}", CountryCode = req.CountryCode ?? "us",
            Competitive = new UserCompetitiveDto { Elo = 1000, Rating = 1000, LastRatingChange = 0 },
            IsBanned = false, ChatBan = false
        };
        if (!store.Users.TryAdd(userId, user))
            return Conflict("User ID already exists");
        store.NotifyDataChanged();
        return Ok(new { userId });
    }

    [HttpPost("users/{userId}")]
    public IActionResult UpdateUser(string userId, [FromBody] UpdateUserRequest req)
    {
        if (!store.Users.TryGetValue(userId, out var user))
            return NotFound();
        if (req.Nick is not null) { user.Nick = req.Nick; foreach (var m in store.ClubMembers.Values) { if (m.TryGetValue(userId, out var mb)) mb.User.Nick = req.Nick; } }
        if (req.CountryCode is not null) user.CountryCode = req.CountryCode;
        if (req.IsProUser.HasValue) user.IsProUser = req.IsProUser.Value;
        if (req.Elo.HasValue) user.Competitive.Elo = req.Elo.Value;
        if (req.Rating.HasValue) user.Competitive.Rating = req.Rating.Value;
        store.NotifyDataChanged();
        return Ok();
    }

    [HttpGet("users")]
    public IActionResult ListUsers()
    {
        var users = store.Users.Values.OrderBy(u => u.Nick).Select(u =>
        {
            string? clubName = null;
            foreach (var (cid, members) in store.ClubMembers)
                if (members.ContainsKey(u.Id) && store.Clubs.TryGetValue(cid, out var c))
                { clubName = c.Name; break; }
            return new { u.Id, u.Nick, u.CountryCode, u.IsProUser, u.Competitive.Elo, u.Competitive.Rating, ClubName = clubName };
        });
        return Ok(users);
    }

    [HttpGet("challenges")]
    public IActionResult ListChallenges()
    {
        return Ok(store.Challenges.Select(c =>
        {
            var scoreCount = store.ChallengeHighscores.TryGetValue(c.Key, out var s) ? s.Count : 0;
            return new { Token = c.Key, c.Value.Map, c.Value.TimeLimit, c.Value.ForbidMoving, c.Value.ForbidRotating, c.Value.ForbidZooming, ScoreCount = scoreCount };
        }));
    }

    [HttpPost("challenges")]
    public IActionResult CreateChallenge([FromBody] CreateChallengeRequest req)
    {
        var token = store.GenerateChallengeToken();
        store.Challenges[token] = new PostChallengeRequestDto
        {
            AccessLevel = 0, ChallengeType = 0, ForbidMoving = req.ForbidMoving,
            ForbidRotating = req.ForbidRotating, ForbidZooming = req.ForbidZooming,
            Map = req.Map ?? "world", TimeLimit = req.TimeLimit > 0 ? req.TimeLimit : 60
        };
        store.ChallengeHighscores[token] = [];
        store.NotifyDataChanged();
        return Ok(new { token });
    }

    [HttpGet("challenges/{token}/scores")]
    public IActionResult GetScores(string token)
    {
        if (!store.ChallengeHighscores.TryGetValue(token, out var scores))
            return NotFound();
        return Ok(scores.Select(s => new
        {
            s.Game.Player.Id, s.Game.Player.Nick,
            Score = s.Game.Player.TotalScore.Amount,
            Distance = s.Game.Player.TotalDistance.Meters.Amount
        }));
    }

    [HttpPost("challenges/{token}/scores")]
    public IActionResult AddScore(string token, [FromBody] AddScoreRequest req)
    {
        if (!store.ChallengeHighscores.TryGetValue(token, out var scores))
            return NotFound();
        var nick = store.Users.TryGetValue(req.UserId, out var u) ? u.Nick : req.UserId;
        scores.Add(new ChallengeResultItemDto
        {
            Game = new ChallengeResultGameDto
            {
                Player = new ChallengeResultPlayerDto
                {
                    Id = req.UserId, Nick = nick,
                    TotalScore = new ChallengeResultPlayerScoreDto { Amount = req.Score.ToString(), Unit = "points" },
                    TotalDistance = new ChallengeResultPlayerDistanceDto
                    {
                        Meters = new ChallengeResultPlayerDistanceMetersDto { Amount = req.Distance.ToString(), Unit = "m" }
                    }
                }
            }
        });
        store.NotifyDataChanged();
        return Ok();
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> ListJobs()
    {
        var scheduler = await schedulerFactory.GetScheduler();
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
        var jobs = new List<object>();
        foreach (var key in jobKeys.OrderBy(k => k.Name))
        {
            var triggers = await scheduler.GetTriggersOfJob(key);
            var trigger = triggers.FirstOrDefault();
            jobs.Add(new
            {
                Name = key.Name,
                NextFire = trigger?.GetNextFireTimeUtc()?.ToString("yyyy-MM-dd HH:mm:ss"),
                LastFire = trigger?.GetPreviousFireTimeUtc()?.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
        return Ok(jobs);
    }

    [HttpPost("jobs/{name}/trigger")]
    public async Task<IActionResult> TriggerJob(string name)
    {
        var scheduler = await schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey(name));
        return Ok();
    }

    [HttpGet("missions")]
    public IActionResult ListMissions()
    {
        List<DailyMissionDto> snapshot;
        lock (store.DailyMissions)
        {
            snapshot = store.DailyMissions.ToList();
        }
        return Ok(new
        {
            nextMissionDate = store.NextMissionDate,
            missions = snapshot
        });
    }

    [HttpPost("missions")]
    public IActionResult AddMission([FromBody] AddMissionRequest req)
    {
        var mission = new DailyMissionDto
        {
            Id = Guid.NewGuid(),
            Type = req.Type,
            GameMode = req.GameMode,
            CurrentProgress = req.CurrentProgress,
            TargetProgress = req.TargetProgress,
            Completed = req.Completed,
            EndDate = req.EndDate ?? DateTimeOffset.UtcNow.Date.AddDays(1),
            RewardAmount = req.RewardAmount,
            RewardType = req.RewardType
        };
        lock (store.DailyMissions)
        {
            store.DailyMissions.Add(mission);
        }
        store.NotifyDataChanged();
        return Ok(new { id = mission.Id });
    }

    [HttpDelete("missions/{id:guid}")]
    public IActionResult RemoveMission(Guid id)
    {
        lock (store.DailyMissions)
        {
            var index = store.DailyMissions.FindIndex(m => m.Id == id);
            if (index < 0)
            {
                return NotFound();
            }
            store.DailyMissions.RemoveAt(index);
        }
        store.NotifyDataChanged();
        return Ok();
    }

    [HttpDelete("missions")]
    public IActionResult ClearMissions()
    {
        lock (store.DailyMissions)
        {
            store.DailyMissions.Clear();
        }
        store.NotifyDataChanged();
        return Ok();
    }

    [HttpPost("missions/next-date")]
    public IActionResult UpdateNextMissionDate([FromBody] UpdateNextMissionDateRequest req)
    {
        store.NextMissionDate = req.NextMissionDate;
        store.NotifyDataChanged();
        return Ok();
    }
}
