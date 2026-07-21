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
    sql_database: str = "DomainLinks"
    sql_driver: str = "ODBC Driver 18 for SQL Server"
    sql_trusted_connection: bool = True
    sql_trust_server_certificate: bool = True
    sql_encrypt: bool = True

    default_llm_provider: str = "ollama"
    ollama_base_url: str = "http://127.0.0.1:11434"
    ollama_embed_model: str = "nomic-embed-text:v1.5"
    ollama_chat_model: str = "llama3.1:8b"
    ollama_generation_model: str = "qwen3.5:35b-mlx"
    ollama_title_model: str = "llama3.1:8b"
    lmstudio_base_url: str = "http://127.0.0.1:1234"

    def public_config(self) -> dict[str, object]:
        return {
            "environment": self.env,
            "api_host": self.api_host,
            "api_port": self.api_port,
            "sql_server": self.sql_server,
            "sql_database": self.sql_database,
            "sql_driver": self.sql_driver,
            "sql_trusted_connection": self.sql_trusted_connection,
            "sql_trust_server_certificate": self.sql_trust_server_certificate,
            "sql_encrypt": self.sql_encrypt,
            "default_llm_provider": self.default_llm_provider,
            "ollama_base_url": self.ollama_base_url,
            "ollama_embed_model": self.ollama_embed_model,
            "ollama_chat_model": self.ollama_chat_model,
            "ollama_generation_model": self.ollama_generation_model,
            "ollama_title_model": self.ollama_title_model,
            "lmstudio_base_url": self.lmstudio_base_url,
        }


@lru_cache
def get_settings() -> Settings:
    return Settings()
