# Prompt: build the KspMp website

Hand this whole file to a fresh session as the brief. It is self-contained: everything the site needs to
say is in here, and nothing outside it should be invented.

---

## The job

Build a website for **KspMp**, a multiplayer mod for Kerbal Space Program 1.12.5. One page is probably
enough, with a couple of deeper pages if the content earns them. It has two readers:

1. **A KSP player who wants to fly with a friend.** They need to know in thirty seconds whether this does
   what they want, what state it is in, and how to get it running with one other person tonight.
2. **Someone who might contribute or just wants to see how it works.** They need the repository, the
   architecture in brief, and an honest list of what is unfinished.

The site is a front door, not documentation. The repository's README stays the deep reference; the site
links to it rather than restating it.

## Ground truth about the mod

This section is the only source for factual claims. If something is not here, do not put it on the site.

- **Name**: KspMp. **Author**: Campbell Scott (GitHub `skelinn`). **Licence**: MIT.
- **Repository**: https://github.com/skelinn/KspMp
- **Game**: Kerbal Space Program **1.12.5** only, the Steam build. Windows is the primary platform;
  macOS works for development.
- **Requires** Harmony (`GameData/000_Harmony`), which ships alongside the mod.
- **Status**: alpha, pre-release. It has been played over Steam between two machines on separate networks,
  and is tested continuously with two game clients driven by a scripted harness. It has not been tested with
  more than two players, on a dedicated public server, or over high latency.

**What it does today**

- Two or more players share one timeline: one clock, one warp rate. Anyone can warp, the slowest wish wins,
  anyone can drop back to 1x.
- Each player is their own Kerbal, claimed from a shared roster.
- **Fly together in one rocket.** Whoever launches is the pilot; anyone else aboard is a co-pilot. Staging,
  action groups, SAS, the navball buttons, part-menu buttons and part-menu sliders all reach the other
  player. The co-pilot's stick is locked until the pilot turns on shared stick, and control can be handed
  over or asked for.
- **Build together.** Everyone gets their own workbench in the VAB/SPH; a Builders list shows who is
  building what, and one click joins someone's bench. Parts, the staging column and the crew tab are shared.
- **See each other.** Other players' craft and Kerbals appear in your sky with name tags, in their real
  positions, running the pilot's throttle, engines, chutes and staging.
- **EVA**, boarding each other's craft, docking (the merged craft goes to whoever docked, the other player
  stays aboard as co-pilot), undocking, decoupling, reverting, and Kerbals who die coming back after a few
  seconds.
- **Hosting**: one player hosts from inside their game (no second program to run), over Steam - which gets
  through NAT that port forwarding could not - or over a plain UDP port for a LAN or a known address. A
  dedicated headless server also exists for anyone who wants one.
- **When something looks wrong**: a Resync button asks the server for the world again, and the mod writes
  its own small log at `GameData/KspMp/PluginData/kspmp.log` to send when reporting a problem.

**What is honestly not there yet**

Say this plainly on the site; do not bury it. A player who finds out the hard way is worse than one who
never installs.

- Alpha software. Expect desync, and expect to restart a session occasionally.
- **Both players must run exactly the same build.** The handshake refuses a mismatch, which is the intended
  behaviour, not a bug.
- **Both players need the same GameData.** The server warns in chat when two players' installed parts
  differ, but a craft using a part the other side does not have will still fail to load on their machine.
  A stock install on both sides is the safe option.
- Tested with two players. More has not been tried.
- A host must know a joining friend's Steam ID beforehand and let it in before they press Join.
- A co-pilot's resource transfers and crew transfers change their own copy only.
- Career-mode progression, science and contracts are not synchronised in any deliberate way.

## What the site must contain

Work out the layout yourself; these are the content obligations, not a section list to transcribe.

- **A one-sentence answer to "what is this".** Something a player understands before scrolling.
- **The three things that sell it**: sit in the same rocket and both fly it, build the same craft in the
  same VAB, see each other's ships and Kerbals in your own sky.
- **Honest status**, above the fold or immediately below it. Alpha, two players, expect rough edges.
- **Requirements**: KSP 1.12.5, Steam, both players on the same build and the same GameData.
- **Install**, short enough to follow without scrolling back: unzip two folders into `GameData`, both
  players install the same zip, launch KSP through Steam.
- **How to play together**: host presses Host and shares their Steam ID; the friend pastes it and presses
  Join; the host must add the friend's ID before that. Then set your name, claim a Kerbal, and go.
- **Download**, prominent, with the build identifier visible so two players can check they match.
- **Troubleshooting**, brief: send `kspmp.log`, press Resync, both on the same build.
- **Links**: repository, issue tracker, licence.

## Rules

**Honesty.** Every claim must trace to the ground-truth section above. No invented features, no roadmap
presented as if it exists, no performance or player-count numbers, no testimonials, no fabricated review
quotes, no screenshots or footage that were not actually captured from the mod. If the site wants a
screenshot and none exists, leave a clearly marked placeholder and tell the user what to capture rather
than generating a fake one.

**Legal.** Kerbal Space Program, Kerbals and Squad are the intellectual property of their owners. Do not use
the KSP logo, Squad or Private Division branding, official artwork, or any Kerbal character art. Do not
imply endorsement, affiliation or that this is official. A plain line such as "an unofficial fan project,
not affiliated with the makers of Kerbal Space Program" belongs in the footer. The mod itself is MIT; say so
and link the licence.

**Downloads.** Do not host a binary the user has not published. Point the download at a GitHub release, and
if no release exists yet, say what needs to happen and leave the button pointing at the repository's
releases page.

## Design direction

The tone is a mission-control readout: dark, precise, a little technical, confident without shouting. It is
a tool for two friends flying a rocket badly together, so it can be warm - just not cartoonish, and never
using Kerbal characters to be warm.

Start from the mod's own in-game palette so the site and the game agree:

| Role | Hex |
|---|---|
| Text | `#C9D2DD` |
| Muted text | `#7B8797` |
| Accent | `#4FD1A5` |
| Warning | `#E8B84B` |
| Error | `#FF6B6B` |

Pick the dark background and any surface tints yourself. A restrained technical typeface for headings, a
plain readable one for body. Motion is welcome where it explains something - two craft in the same sky, a
control handover - and unwelcome as decoration. Every animated piece needs a `prefers-reduced-motion` path.

## Build

Follow the global front-end rules already loaded in this session for the component and animation stack, the
registry setup and the licence checks. Beyond that:

- The site must deploy as static files. GitHub Pages on the existing repository is the obvious host, so
  keep the build output committable and the base path configurable.
- No backend, no analytics that need consent, no third-party fonts that cannot be self-hosted.
- Accessible by default: real headings, keyboard reachable, contrast checked against the palette above,
  and it must read correctly in both a maximised window and on a phone.

## Before you start, ask the user

Keep it to these, and pick sensible defaults for everything else:

1. Where does the site live - GitHub Pages on `skelinn/KspMp`, or a domain?
2. Should the download point at a GitHub release now, or at the releases page until the first one is cut?
3. Are there screenshots or a clip to use, or should the visual slots stay as marked placeholders?
4. Is the friend's-Steam-ID flow the one to document, or is direct connect the headline?

## Done means

- Builds clean, deploys as static files, and works with JavaScript doing nothing but enhancement.
- Every factual sentence traces to the ground-truth section.
- The unfinished list is visible without hunting for it.
- No KSP or Squad branding anywhere, and an unaffiliated notice in the footer.
- Contrast and keyboard navigation pass; reduced motion is respected.
- A person who has never heard of the mod can read the page and get two machines flying one rocket
  without opening the repository.
