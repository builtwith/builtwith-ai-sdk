# Python

The [official Python SDK](https://github.com/builtwith/builtwith-python) provides typed
synchronous and asynchronous REST clients and integrates with the existing BuiltWith
MCP server, alongside the Node.js and .NET SDKs.

```bash
pip install builtwith-official
# To use the existing MCP server from Python:
pip install "builtwith-official[mcp]"
```

```python
from builtwith_official import BuiltWith

with BuiltWith() as client:
    print(client.vat_types().data)  # Public; no key or credits
```

Set `BUILTWITH_API_KEY` for authenticated calls. For MCP:

```python
from builtwith_official.mcp import connect_mcp

async def discover_tools() -> None:
    async with connect_mcp() as session:
        print(await session.list_tools())
```

See [MCP integration](https://github.com/builtwith/builtwith-python/blob/main/docs/mcp.md)
for tool calls against `https://api.builtwith.com/mcp`. Python does not create a new
server or change the Node/.NET build processes.
