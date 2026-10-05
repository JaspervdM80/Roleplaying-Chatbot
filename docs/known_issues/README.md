# Known issues

Traps that already cost someone time, one entry each: the symptom, the cause, and the rule that keeps
it from coming back. Not a changelog. Add an entry in the same change that fixes a non-obvious bug.

## EF Core check constraints are missing from `db.Model`

**Symptom:** a test asserting `GetCheckConstraints()` on `db.Model` finds none, although the migration
creates the constraint.
**Cause:** the runtime model is read-optimised and drops design-time-only metadata.
**Rule:** read check constraints from `db.GetService<IDesignTimeModel>().Model`.
