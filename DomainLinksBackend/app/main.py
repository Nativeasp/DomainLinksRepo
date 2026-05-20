from fastapi import FastAPI

from app.config import get_settings


def create_app() -> FastAPI:
    settings = get_settings()
    app = FastAPI(
        title="DomainLinks Backend",
        version="0.1.0",
        description="SQL Server backed retrieval and local AI provider service.",
    )

    @app.get("/health")
    def health() -> dict[str, object]:
        return {
            "status": "ok",
            "environment": settings.env,
            "default_provider": settings.default_llm_provider,
            "sql_server": settings.sql_server,
            "sql_database": settings.sql_database,
        }

    return app


app = create_app()
