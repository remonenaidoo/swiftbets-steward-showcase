-- Unresolved incidents, plus resolved (or, for staff, any) since @Since; newest first.
SELECT incident_id AS Id, title AS Title, impact AS Impact, status AS Status, components AS Components,
       created_at AS CreatedAt, updated_at AS UpdatedAt, resolved_at AS ResolvedAt, created_by AS CreatedBy
FROM status.incidents
WHERE resolved_at >= @Since OR (resolved_at IS NULL AND (@AllUnresolved OR created_at >= @Since))
ORDER BY created_at DESC
LIMIT 200;
