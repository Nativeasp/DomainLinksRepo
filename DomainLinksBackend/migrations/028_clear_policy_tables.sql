SET NOCOUNT ON;
GO

DELETE FROM dbo.PolicyControlStatements;
DELETE FROM dbo.PolicyPrincipleLinks;
DELETE FROM dbo.PrincipleRelations;
DELETE FROM dbo.PolicyObjectives;
DELETE FROM dbo.PolicyPrinciples;
DELETE FROM dbo.PolicyAccountabilityStatements;
DELETE FROM dbo.PolicyTransparencyStatements;
DELETE FROM dbo.PolicyStrategyStatements;
DELETE FROM dbo.PolicyConsequences;
DELETE FROM dbo.PolicySections;
DELETE FROM dbo.Policies;
DELETE FROM dbo.Principles;
DELETE FROM dbo.PolicyTemplates;
GO

SELECT 'PolicyTemplates' AS TableName, COUNT(*) AS TotalRows FROM dbo.PolicyTemplates
UNION ALL SELECT 'Policies', COUNT(*) FROM dbo.Policies
UNION ALL SELECT 'PolicySections', COUNT(*) FROM dbo.PolicySections
UNION ALL SELECT 'PolicyObjectives', COUNT(*) FROM dbo.PolicyObjectives
UNION ALL SELECT 'PolicyPrinciples', COUNT(*) FROM dbo.PolicyPrinciples
UNION ALL SELECT 'PolicyAccountabilityStatements', COUNT(*) FROM dbo.PolicyAccountabilityStatements
UNION ALL SELECT 'PolicyTransparencyStatements', COUNT(*) FROM dbo.PolicyTransparencyStatements
UNION ALL SELECT 'PolicyStrategyStatements', COUNT(*) FROM dbo.PolicyStrategyStatements
UNION ALL SELECT 'PolicyConsequences', COUNT(*) FROM dbo.PolicyConsequences
UNION ALL SELECT 'Principles', COUNT(*) FROM dbo.Principles
UNION ALL SELECT 'PolicyPrincipleLinks', COUNT(*) FROM dbo.PolicyPrincipleLinks
UNION ALL SELECT 'PrincipleRelations', COUNT(*) FROM dbo.PrincipleRelations
UNION ALL SELECT 'PolicyControlStatements', COUNT(*) FROM dbo.PolicyControlStatements
ORDER BY TableName;
GO
