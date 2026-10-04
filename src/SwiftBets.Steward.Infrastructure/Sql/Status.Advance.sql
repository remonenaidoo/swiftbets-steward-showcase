-- Moves an unresolved incident on; no row means it was already resolved (or does not exist).
UPDATE status.incidents
SET status = @Status, updated_at = @At, resolved_at = CASE WHEN @Status = 'resolved' THEN @At ELSE NULL END
WHERE incident_id = @Id AND resolved_at IS NULL;
