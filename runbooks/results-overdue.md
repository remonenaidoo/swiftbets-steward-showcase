---
id: results-overdue
title: Results overdue (finished fixtures never resulted)
---
# Results overdue

## Symptoms
- `OfferResultsOverdue`: `swiftbets_offer_results_overdue` above zero for five minutes. Fixtures finished more than five minutes ago have no published result.
- `OfferFeedStalled`: the offer feed has not completed a tick in over five minutes.
- Punter-facing: bets on finished fixtures stay `open`; settlement has no result for those fixtures.

## Evidence to collect
- Whether the offer is up and its feed ticks are completing (`swiftbets_offer_feed_last_tick_timestamp_seconds`, "Feed tick failed" in its logs).
- The overdue fixtures: compare `offer.result-published` with settlement's `Results` for fixtures that have open legs.
- Whether the live feed changed (for example `Espn:Enabled` taking over from the replays) while coupons on replay fixtures were still open.

## Diagnosis
- **Offer was down or its ticks stalled:** each tick publishes every result due since the durable watermark `offer:results-through`, up to seven days back (D202), so recovery is automatic once ticks complete.
- **Outage longer than seven days, a window played at an earlier `Replay:SlotSeconds`, or the replays no longer the live feed:** recovery needs an operator catch-up.
- **Settlement has the result but coupons are still open:** not this runbook; see `stuck-coupon`.

## Remediation
- Restore the feed first (restart the offer, or fix whatever fails the tick).
- For anything automatic recovery does not cover, call `POST /admin/trading/results/catch-up {from, to, slotSeconds?}` (`trading:write`). It publishes the feed's own recorded scores. It is safe to repeat: settlement ignores a result version it already holds.

## Verification
- `swiftbets_offer_results_overdue` is 0, and settlement has a result for every fixture with open legs that has finished.
- Bet history shows no open coupons on those fixtures, and their winners are paid (see `payout-dead-letters`).
