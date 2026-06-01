SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DELETE FROM dbo.DomainControls;
DELETE FROM dbo.Controls;

SELECT COUNT(*) AS DomainControlsCount FROM dbo.DomainControls;
SELECT COUNT(*) AS ControlsCount FROM dbo.Controls;
SELECT COUNT(*) AS ControlTypesCount FROM dbo.ControlTypes;
