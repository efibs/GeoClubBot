# 🌍 GeoClubBot — Command Guide

Hey everyone! This bot helps us run our GeoGuessr club: tracking activity, linking accounts, sending reminders, and more. Below is a full rundown of every command you can use, written so you can follow along whether you've never touched a slash command before or you're already a Discord pro.

---

## 📚 First Things First — How Commands Work

This bot uses two kinds of interactions: **slash commands** and **user commands**. Here's how to use each.

### 1. Slash Commands
Slash commands are the main way to interact with the bot. To use one:
1. Click in the message box at the bottom of any channel (in this server).
2. Type `/` — a popup appears with a list of available commands.
3. Start typing the command name (e.g. `daily-reminder`) and Discord will filter the list.
4. Click the command, then fill in any parameters Discord asks you for.
5. Press **Enter** to send it.

Commands in this bot are grouped under a prefix (for example, everything for reminders lives under `/daily-reminder`). When you type `/daily-reminder`, Discord will show you the available sub-commands like `add`, `remove`, `clear`, or `list`.

Most of the bot's replies are **ephemeral** — meaning only *you* can see the response. So feel free to experiment without spamming the channel.

### 2. User Commands (right-click menu)
Some commands can be triggered directly on another person:
1. **Right-click** (or long-press on mobile) a user, anywhere — in chat, the member list, etc.
2. Hover over **Apps**.
3. Pick the command you want to run on that user (e.g. `gg-nickname`).

User commands are basically a shortcut that runs a slash command with that user pre-filled as the parameter.

The same **Apps** menu appears when you right-click a **message** rather than a user — that is how
you leave written feedback on one of the bot's AI answers.

---

# ✨ Features & Commands

Below, commands are grouped by feature so you can find what you need quickly.

---

## 🔗 Feature: Linking Your GeoGuessr Account
Many of the other features only work once your Discord account is linked to your GeoGuessr account. Linking is a one-time process. It uses a one-time password that you send to an admin **inside GeoGuessr** to prove the account is really yours.

### `/gg-account link`
Starts the linking process for your account.

**Parameters:**
- `shareProfileLink` *(required)* — the share link to your GeoGuessr profile. It should look like `https://www.geoguessr.com/user/62c353a29d0d57e7b9a3383f`.
  - To get this link: open GeoGuessr → top right → **Profile** → click the **share button** to the left of *EDIT AVATAR* → copy the link.

**What happens next:** The bot replies (only to you) with a one-time password. **Send that password as a direct message to an admin *inside GeoGuessr*** (not in Discord!). An admin will then confirm the link and you'll be notified.

---

## ⏰ Feature: Daily Reminder
Reminds you (via DM) every day to earn your club XP, at times you choose. It covers the two things
you can do each day:

- **keep your streak** — play the **daily challenge** or a **duel** (UTC day), and
- **claim a club mission** — when a mission on the club's board is still free and you haven't used
  today's claim yet. If you're still holding a mission from an earlier day, it reminds you to
  **finish it (or request help)** instead, naming the mission and your progress.

The message names only what you still owe, and no DM is sent at all once nothing is left. The claim
day resets at 12:00 UK time (11:00 UTC in summer) — the reminder tells you when. If your GeoGuessr
account isn't linked, the bot can't see your activity and reminds you of both in general terms.

You can set up **several reminders** (for example one in the morning and a follow-up in the evening), each with its own time and message. Reminders are sent as direct messages from the bot. If the bot happens to be offline right when a reminder is due (for example during an update), it catches up as soon as it's back online: you'll get the missed reminder shortly after the bot starts — at most one catch-up message, even if the bot was down for a long time or you missed several reminder times that day.

### `/daily-reminder add`
Adds a new reminder (or updates the one already set at that time).

**Parameters:**
- `time` *(required)* — the time you want to be reminded, in 24-hour `HH:mm` format. Example: `09:00`, `21:30`.
- `timezone` *(optional)* — an IANA timezone ID, e.g. `Europe/Berlin`, `America/New_York`, `Asia/Tokyo`. If you leave it blank, the bot uses **UTC**.
- `message` *(optional)* — your own reminder message. If you leave it blank, the bot uses its default message, which already names what you still owe.

**Showing what you still owe in your own message**
If you write your own message, you can choose **where** that goes. Just type `{{outstanding_text}}` (copy it exactly, with the double curly braces) at the spot where it should appear. When the reminder is sent, the bot replaces `{{outstanding_text}}` with what is actually left for you that day, starting with a verb ("play the daily challenge …", "claim a club mission …") — so write "Don't forget to {{outstanding_text}}" rather than "Don't forget {{outstanding_text}}".

- ✅ If you include `{{outstanding_text}}`, it appears right there.
- ⚠️ If you **don't** include `{{outstanding_text}}` in your custom message, only your own text will be sent.
- 💡 It finishes the sentence (with "!"), so it reads best at the **end** of your message.
- ℹ️ `{{mission_text}}` from older reminders still works — it inserts the same text.

**Example** — you set this custom message:

```
Time for GeoGuessr! 🌍 Don't forget to {{outstanding_text}}
```

The DM you actually receive looks like this:

```
Time for GeoGuessr! 🌍 Don't forget to play the daily challenge (or a duel) and claim a club mission (5 still free on board 3, the daily claim resets in 4 hours)!
```

Once you've claimed a mission, the same reminder that evening says:

```
Time for GeoGuessr! 🌍 Don't forget to play the daily challenge (or a duel)!
```

### `/daily-reminder remove`
Removes one of your reminders. The `reminder` parameter offers a pick-list of your existing reminders (shown by time), so you just choose the one to delete.

### `/daily-reminder clear`
Removes **all** of your daily reminders at once. No parameters.

### `/daily-reminder list`
Lists all of your reminders: their times, timezones, custom messages, and when each was last sent. No parameters.

---

## 📊 Feature: Your Personal Activity
See how you're doing in the club.

### `/my-activity current-week`
Shows your progress since the club's last weekly check — which runs right after GeoGuessr's weekly board reset, so this is the week the next check will judge:

- **XP earned** (and your **rule XP**, the figure averages and swaps use: without the board-clear bonus and with at most 3 missions a week),
- **streak days** and **club missions** finished (missions you claimed; helping doesn't count),
- the club's **requirements** and where you stand, e.g. `✅ streak 6/6 · ❌ missions 1/2`,
- how many missions of others you pressed **help out** on, this and last board week (for information only — pressing the button proves nothing),
- a day-by-day strip: 🟩 streak kept, ⬛ not kept, and a digit for the missions finished that day.

No parameters. Requires your GeoGuessr account to be linked (see `/gg-account link`).

### `/my-activity last-days`
Shows your streak, club missions and XP over the last several days — handy for a rolling window instead of the current check week. (The requirements only apply to the check week, so they aren't shown here.)

**Parameters:**
- `days` *(optional)* — how many days back to include. Defaults to `7`; the longest window is set by the admins (14 days by default, since GeoGuessr's activity feed only reaches back about two weeks).

Requires your GeoGuessr account to be linked (see `/gg-account link`).

---

## 🏆 Feature: Club Stats
Check how the club as a whole is performing.

### `/club-stats todays-xp`
Shows how much XP a club has earned today (UTC), how many members kept their streak, how many board missions were finished, and how many members claimed a mission in the current claim cycle.

**Parameters:**
- `clubName` *(optional)* — the name of the club. If left blank, the default club is used.

### `/club-stats board`
Shows this week's club mission boards: when the week ends and when the daily claims reset, each board (✅ cleared, ▶️ current with its progress and free missions, 🔒 locked), and every **open mission** — who holds it, the mission, the progress, when it was claimed, and 🆘 if they asked for help. Handy to see what's blocking the next board.

**Parameters:**
- `club` *(optional)* — pick a club from the suggestions. If left blank, the main club is used.

---

## 🛡️ Feature: Member Activity (admins)
These require the **Administrator** permission. Replies are only visible to you.

### `/member-activity current-week by-nickname|by-user` · user command **Current Week XP**
The same view as `/my-activity current-week`, for any member: streak, club missions, requirements and helps since the last weekly check.

### `/member-activity last-days by-nickname|by-user` · user command **Last 7 Days XP**
The same view as `/my-activity last-days`, for any member.

**Parameters:** `nickname` (with suggestions) or `user`, and `days` *(optional, default 7)*.

### `/member-activity inactive-members`
Two lists for today: who hasn't played the daily challenge (or a duel) yet, and who could still claim a club mission in the current claim cycle but hasn't. Linked members are shown with their Discord handle, but nobody is pinged.

**Parameters:** `club` *(optional)* — defaults to the main club.

### Automatic posts
- **Weekly activity check** — right after GeoGuessr's weekly board reset, the bot lists everyone who missed a requirement (e.g. `missed streak 4/6 · missions 1/2`) and hands out strikes; members who joined during the week or were excused get proportionally lower targets.
- **Swap suggestions** *(if enabled)* — after the check: which second-club members should replace kicked main-club members, fill free spots, or swap with a main-club member whose average rule XP is lower by at least the buffer. Suggestions only; the bot never moves anyone.
- **Stuck-mission alerts** *(if enabled)* — when a claimed mission has been open too long, or its claimer asked for help, while it holds up the board. Each mission is alerted about once.

---

## 👤 Feature: User Info
Look up information about other members and connect Discord ↔ GeoGuessr identities.

### `/user-info gg-nickname`
Tells you what GeoGuessr nickname a Discord user is linked to.

**Parameters:**
- `user` *(required)* — pick a member of the server.

Also available as a **user command**: right-click a member → **Apps** → **GeoGuessr Nickname**.

### `/user-info gg-profile`
Shows a full GeoGuessr profile for a Discord user — country, member-since date, account type, level, rating, status (good standing / banned / suspended / chat banned), and their club.

**Parameters:**
- `user` *(required)* — pick a member of the server.

Also available as a **user command**: right-click a member → **Apps** → **GeoGuessr Profile**.

### `/user-info gg-ranked`
Shows a GeoGuessr ranked-stats card for a Discord user — division, current and peak rating per game mode (overall / move / no-move / NMPZ), win streak, guessed-first rate, a visualization of their recent games (🟩 won / 🟥 lost), and their best and worst countries by flag.

**Parameters:**
- `user` *(required)* — pick a member of the server.

Also available as a **user command**: right-click a member → **Apps** → **GeoGuessr Ranked Stats**.

### `/user-info discord-user`
The reverse lookup: give it a GeoGuessr nickname and it tells you which Discord user that is.

**Parameters:**
- `nickname` *(required)* — the GeoGuessr nickname (case-sensitive).

---

## 🗺️ Feature: Country Challenges
On fixed days the bot posts themed country challenges — like *Mongolia Monday* or *Small Country
Sunday* — as GeoGuessr challenge links. Play them like any other challenge: finish all five rounds to
count.

A day or so later the bot posts the results. The top places earn **points** (3 for first, 2 for second,
1 for third, unless your server set it up differently), and sometimes a role. The points add up to a
**leaderboard** that the bot posts regularly, usually once a week.

This feature may be switched off on your server.

### `/country-challenges leaderboard`
Shows the current season's leaderboard and **your own place** on it, even if you are not in the top
places. Players with the same number of points share a place.

No parameters.

> Your place is only shown if your Discord account is linked to your GeoGuessr account — see
> `/gg-account link`. Linking is also what lets the bot give you a winner's role.

### Admin commands
These require the **Administrator** permission:

- `/country-challenges-admin preview` — checks the challenge configuration and shows what the bot
  would post on a day, **without posting anything or pinging anyone**. Parameter: `day` — a date
  (`2026-12-24`) or a weekday (`sunday`); default today.
- `/country-challenges-admin post-now` — runs today's country challenges right away: results that are
  due, the leaderboard if it's due, and today's challenges. Anything already posted today is skipped,
  so it's safe to run again, for example after the bot was offline at the scheduled time.
- `/country-challenges-admin results-now` — evaluates every challenge still waiting for its results
  right now instead of on its day: posts the results, awards the points and hands out the roles. A
  challenge nobody has played yet closes with "No one participated", so use it once the players are done.
- `/country-challenges-admin import-standings` — opens a form to paste the standings kept by hand
  before the bot tracked them: one player per line, GeoGuessr nickname (or profile link) followed by
  points, like `Fibs 12`. Nothing is imported unless every line names exactly one player, and importing
  again replaces the previous import.

How to set the challenges up is described in the
[Country Challenges Guide](Documentation/CountryChallengesGuide.md).

---

## 🎭 Feature: Self-Roles
Pick optional roles for yourself (e.g. notification opt-ins, regional roles) without needing an admin to assign them.

### `/self-roles select`
Opens a private menu where you can tick or untick each available role. Roles you already have appear pre-selected; choose the final set you want and confirm. The bot updates your roles and tells you what changed.

No parameters.

---

## 🤖 Feature: AI Assistant
Ask the bot about GeoGuessr metas — bollards, poles, plates, road markings, scripts — and it answers
from an indexed library of community guides, showing the guide images that make the point better than
words do.

This feature is optional and may be switched off on your server. If it is off, `/ai status` will say so.

### Asking a question
There is no command for this — just **@-mention the bot** in a message:

> @GeoClubBot what do Ghanaian bollards look like?

You can attach a screenshot to your message and ask about that instead. The bot replies in the
channel (not privately, so everyone can learn from the answer), cites the guides it used, and attaches
any guide images it relied on.

### Asking a follow-up
**Reply to the bot's answer** and ask your next question. You don't need to mention it again — it
picks up where the conversation left off.

Several people can reply to the same answer at once. Each reply starts its own branch of the
conversation, so your follow-ups and someone else's never get mixed together. If you reply to
*someone else's* follow-up, you join their branch and see its history.

A conversation goes quiet after a day of inactivity; replying after that starts a fresh one. Very long
threads get a nudge suggesting you start over, which keeps answers sharp.

### Rating an answer
Every answer comes with 👍 and 👎 on it. Click one — that is the whole thing. Clicking the other one
changes your mind; clicking the same one again takes your rating back.

To say *why*, **right-click the answer** → **Apps** → **👍 Good AI answer** or **👎 Bad AI answer**,
and a box opens for a comment. The comment is optional.

Anyone can rate an answer, and everyone's rating is counted separately. Once an answer has been rated
the bot marks it with ✅.

> Rating an answer is what makes the bot **keep** that conversation. Unrated conversations are deleted
> after a while; rated ones are saved so the answers can be improved. Taking your rating back deletes
> the saved copy again.

### `/ai search`
Shows what the guide library returns for a query, **without** asking an AI model to write an answer.
Useful for finding the source guide itself, and for checking whether the bot actually has anything
on a topic.

**Parameters:**
- `query` *(required)* — what to look for.
- `country` *(optional)* — restrict results to one country.

### `/ai status`
Shows which AI models are currently available, how much of the guide library is indexed, and how much
of today's request allowance is left. Models excluded at runtime for answering with something that is
not an answer are listed too, until the bot restarts.

No parameters.

> **Note on limits.** The AI runs on a free allowance that resets daily. If it says it's out of
> requests for the day, that's expected rather than broken — it resets at 00:00 UTC. There is also a
> per-person hourly cap so one enthusiastic user can't spend the whole server's budget.

### Admin commands
These require the **Administrator** permission:

- `/ai sync-sources` — refresh the catalogue of known guide sources.
- `/ai ingest` — index a batch of guides now. Parameters: `count`, `source-type`, `force`.
- `/ai feedback` — how answers have been rated: totals, a split by model, and recent comments.
  Parameter: `days` (0 counts everything).
- `/ai feedback-export` — download the rated conversations as a JSONL file, for working out what to
  improve. Parameters: `rating` (good/bad), `days`.

Indexing normally runs by itself overnight, so these are only needed to kick things along or after
changing what the bot should read.

---

# 💡 Tips
- All bot replies are **only visible to you** unless stated otherwise — so don't worry about cluttering channels.
- If a command fails with an "internal error" message, try again later. If it keeps happening, ping an admin.
- Commands and their parameters auto-complete as you type, so you don't need to memorize anything — just type `/` and explore.
