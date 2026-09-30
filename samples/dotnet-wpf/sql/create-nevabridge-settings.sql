-- Holds the NevaBridge connection settings the desktop application reads at startup.
--
-- Users never get SELECT on the table. Members of NevaBridgeReader may only run
-- dbo.GetNevaBridgeSettings, which reads the table through ownership chaining. This keeps the
-- key out of ad-hoc queries and exports, but anyone who can run the application can still
-- obtain it. See the sample README before using this pattern in production.

CREATE TABLE dbo.NevaBridgeSettings (
    Id tinyint NOT NULL
        CONSTRAINT PK_NevaBridgeSettings PRIMARY KEY
        CONSTRAINT CK_NevaBridgeSettings_SingleRow CHECK (Id = 1),
    BaseUrl nvarchar(200) NOT NULL,
    ProductId nvarchar(100) NOT NULL,
    ApiKey nvarchar(200) NOT NULL,
    -- Where the web chat page is loaded from. NULL uses the page shipped with the application.
    WebChatUrl nvarchar(400) NULL
);
GO

CREATE ROLE NevaBridgeReader;
GO

CREATE PROCEDURE dbo.GetNevaBridgeSettings
AS
BEGIN
    SET NOCOUNT ON;
    SELECT BaseUrl, ProductId, ApiKey, WebChatUrl FROM dbo.NevaBridgeSettings WHERE Id = 1;
END
GO

GRANT EXECUTE ON dbo.GetNevaBridgeSettings TO NevaBridgeReader;
GO
