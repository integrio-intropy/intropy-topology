# 0012: The development manifest owns the local re-run cadence

> Status: implemented.

**Background.** The Aspire host re-runs run-to-completion components so file-driven
integrations keep sweeping during local development: when a component's project reaches
Finished/Exited, the host starts the project and its sidecar again after a delay. That
delay was hardcoded to one minute — with no way to tune it for a slower sweep under
debugging or a faster loop against a scratch folder.

The obvious home for the knob — the development manifest — was occupied territory. ADR
0010 removed `WithSchedule` and stated the schedule returns to the component scaffold "and
nowhere else: the model does not re-home it into the development manifest, because that
would split the fact (one cron for F5, another in GitOps)." But 0010's premise was that
"local runs still start every component once; recurring activation ... is a scaffold
concern." The re-run scheduler retired that premise: local recurring activation exists as
host mechanism, and its cadence is a real fact in want of a home.

The split-fact danger 0010 guarded against does not apply here. The fact deployment owns
is the deployed CronJob schedule; the local delay repeats nothing — there is no cron
locally, and nothing in GitOps claims the local loop's cadence. What must not happen is
the local delay *traveling*: if generated artifacts carried it, deployment would inherit a
tuning value and the drift 0010 feared would arrive through the back door.

**Decision.** The development manifest may declare each run-to-completion component's
local re-run delay; undeclared components keep the host's one-minute default.

- **The DSL speaks delays, not cron.** The host mechanism is "start again once the delay
  has passed after a run completes." A cron expression would describe a mechanism the host
  does not have.
- **Validation shares the host's derivation.** The component must exist in the topology
  and must run to completion — the same kind rule the host schedules by, held in one place
  so the two cannot drift. A resident component has no run to repeat, so declaring a
  cadence for one is an error, not a silent no-op.
- **Local-only by construction.** Generation never reads the manifest's re-run
  declarations; no artifact, graph output, or GitOps file can carry them. The deployed
  schedule stays exactly where 0010 put it.

**Consequences.** 0010 is narrowed, not reversed: its "nowhere else" fragment is
superseded for the local re-run delay, while everything it decided about the deployed
schedule — the topology model and the generated artifacts stay silent — stands. Its
"local runs start every component once" premise was already superseded by the scheduler.
Developers tune their own loop in the development definition; deployment configuration
remains the single home of every schedule that reaches a cluster.
