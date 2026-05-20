from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="DOMAINLINKS_",
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    env: str = "development"
    api_host: str = "127.0.0.1"
    api_port: int = 5056

    sql_server: str = "RICHARDBASQB378"
    sql_database: str = "ENTERPRISE_ORIGIN"
    sql_driver: str = "ODBC Driver 18 for SQL Server"
    sql_trusted_connection: bool = True
    sql_trust_server_certificate: bool = True

    default_llm_provider: str = "ollama"
    ollama_base_url: str = "http://127.0.0.1:11434"
    ollama_embed_model: str = "nomic-embed-text:v1.5"
    ollama_chat_model: str = "gemma3:1b"
    lmstudio_base_url: str = "http://127.0.0.1:1234"


@lru_cache
def get_settings() -> Settings:
    return Settings()
