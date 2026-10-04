SELECT incident_id AS Id, title AS Title, impact AS Impact, status AS Status, components AS Components,
       created_at AS CreatedAt, updated_at AS UpdatedAt, resolved_at AS ResolvedAt, created_by AS CreatedBy
FROM status.incidents
WHERE incident_id = @Id;
