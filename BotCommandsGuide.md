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
Reminds you (via DM) every day to earn your club XP, at times you choose. There are **two** ways to
earn it and each is worth 20 XP, so a reminder only stops once you've done **both**: completing the
**daily mission**, and playing the **daily challenge** or a **duel**. The message names
whichever one you still owe, and when the mission is one of them it spells out **today's actual missions** (for example "Play the Daily Challenge" or "Win 5 Team Duels") instead of just saying "your daily mission" — once you've done the mission those are gone from the message and only the daily challenge is named. You can set up **several reminders** (for example one in the morning and a follow-up in the evening), each with its own time and message. Reminders are sent as direct messages from the bot. If the bot happens to be offline right when a reminder is due (for example during an update), it catches up as soon as it's back online: you'll get the missed reminder shortly after the bot starts — at most one catch-up message, even if the bot was down for a long time or you missed several reminder times that day.

### `/daily-reminder add`
Adds a new reminder (or updates the one already set at that time).

**Parameters:**
- `time` *(required)* — the time you want to be reminded, in 24-hour `HH:mm` format. Example: `09:00`, `21:30`.
- `timezone` *(optional)* — an IANA timezone ID, e.g. `Europe/Berlin`, `America/New_York`, `Asia/Tokyo`. If you leave it blank, the bot uses **UTC**.
- `message` *(optional)* — your own reminder message. If you leave it blank, the bot uses its default message, which already names what you still owe.

**Showing what you still owe in your own message**
If you write your own message, you can choose **where** that goes. Just type `{{outstanding_text}}` (copy it exactly, with the double curly braces) at the spot where it should appear. When the reminder is sent, the bot replaces `{{outstanding_text}}` with what is actually left for you that day — and with today's missions listed underneath, whenever the daily mission is still one of them.

- ✅ If you include `{{outstanding_text}}`, it appears right there.
- ⚠️ If you **don't** include `{{outstanding_text}}` in your custom message, only your own text will be sent.
- 💡 It finishes the sentence and can run over several lines, so it reads best at the **end** of your message.
- ℹ️ `{{mission_text}}` from older reminders still works — it inserts the same text.

**Example** — you set this custom message:

```
Time for GeoGuessr! 🌍 Don't forget {{outstanding_text}}
```

The DM you actually receive looks like this:

```
Time for GeoGuessr! 🌍 Don't forget your daily mission and the daily challenge (or a duel) today:
- Play the Daily Challenge
```

Once you've completed the mission, the same reminder that evening says:

```
Time for GeoGuessr! 🌍 Don't forget the daily challenge (or a duel) today!
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
Shows your progress for the current week: total XP earned, how many days you earned **both** of the day's awards, a breakdown per award, and a visual progress bar for each day — 🟩 both done, 🟨 one of the two, ⬛ neither.

The XP figure covers **daily** activity only — weekly missions are left out, since a single one is worth 1000 XP and would drown out everything else. `/club-stats todays-xp` does the same (its `includeWeeklies` option is off by default).

No parameters. Requires your GeoGuessr account to be linked (see `/gg-account link`).

### `/my-activity last-days`
Shows the same progress over the last several days — handy if you want to see a rolling window instead of just the current calendar week.

**Parameters:**
- `days` *(optional)* — how many days back to include, from `1` to `14`. Defaults to `7`.

Requires your GeoGuessr account to be linked (see `/gg-account link`).

---

## 🏆 Feature: Club Stats
Check how the club as a whole is performing.

### `/club-stats todays-xp`
Shows how much XP a club has earned today, plus how many members earned each of the two daily awards (the daily mission, and the daily challenge or a duel) — they're counted separately because a member can do one, both, or neither.

**Parameters:**
- `clubName` *(optional)* — the name of the club. If left blank, the default club is used.
- `includeWeeklies` *(optional, true/false)* — whether to include XP from weekly challenges. Defaults to `false`.

---

## 📈 Feature: Daily Mission Statistics
Curious which daily missions show up the most, how big they usually are, or how often the club actually finishes them? This command crunches the bot's daily mission history for you.

### `/daily-missions stats`
Shows an overview table with one row per mission kind (for example "Win Duels" or "Score points in Classic"): how often it appeared, on what share of days, the average target count (e.g. how many duels you have to play), the club's average completion rate on the days it appeared, and when it was last seen.

The summary above the table also reports how often the club played the **daily challenge** (or a duel) — the club's other daily XP award. That figure only covers days the bot has tracked it; the footer names the first such day.

**Parameters:**
- `days` *(optional)* — how many days back to include, from `1` to `365`. Defaults to `30`.
- `mission` *(optional)* — pick one mission kind from the suggestions to get a detailed view of just that mission instead of the overview table.
- `club` *(optional)* — pick a club from the suggestions to compute the completion rate for that club only. If left blank, all tracked clubs are combined.

The reply is ephemeral — only you can see it, so feel free to experiment.

> ℹ️ Completion rates are computed from a daily snapshot the bot started taking when this feature was released. Days before that show `—` (no data), so the completion column fills up over time.

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
of today's request allowance is left.

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
