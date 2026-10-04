INSERT INTO status.incidents (incident_id, title, impact, status, components, created_at, updated_at, resolved_at, created_by)
VALUES (@Id, @Title, @Impact, @Status, @Components, @CreatedAt, @UpdatedAt, NULL, @CreatedBy);
