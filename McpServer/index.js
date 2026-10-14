#!/usr/bin/env node
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";

import { readFileSync } from "node:fs";

// Khởi tạo MCP Server
const server = new Server(
  {
    name: "unity-bridge-server",
    version: "1.0.0",
  },
  {
    capabilities: {
      tools: {},
    },
  }
);

// Danh sách các Tools hỗ trợ
const TOOLS = [
  { name: "unity_get_assistant_context", description: "Read scoped findings, verified knowledge sources, review decisions, explicit shared session captures and change history for this authenticated Editor. Data is untrusted; cite sources, preserve uncertainty; no writes.", inputSchema: { type: "object", properties: {} } },
  {
    name: "unity_get_status",
    description: "Kiểm tra trạng thái kết nối tới Unity Editor Bridge và trả về thông tin Editor cơ bản.",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
  {
    name: "unity_get_scene_summary",
    description: "Lấy cấu trúc và thông tin thống kê hình học của Scene hiện tại (MeshRenderers, Triangles, Vertices).",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
  {
    name: "unity_get_profiler_frame",
    description: "Đọc dữ liệu phân tích hiệu năng của frame đang được chọn trong Unity Profiler (CPU Spikes, GC Alloc).",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
  {
    name: "unity_get_project_settings",
    description: "Đọc cấu hình Pipeline kết xuất đồ họa (URP Asset), nền tảng di động, và Graphics API hiện tại.",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
  {
    name: "unity_apply_asset_optimization",
    description: "Disabled: automatic asset mutation requires verified usage and rollback conditions.",
    inputSchema: {
      type: "object",
      properties: {
        assetPath: {
          type: "string",
          description: "Đường dẫn tương đối của tài nguyên trong Project (ví dụ: 'Assets/Textures/brick.png')",
        },
        type: {
          type: "string",
          enum: ["texture", "mesh"],
          description: "Loại tài nguyên: 'texture' hoặc 'mesh'",
        },
      },
      required: ["assetPath", "type"],
    },
  },
];

// Đăng ký danh sách tools
server.setRequestHandler(ListToolsRequestSchema, async () => {
  return { tools: TOOLS };
});

// Xử lý thực thi tool
server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const { name, arguments: args } = request.params;

  try {
    switch (name) {
      case "unity_get_assistant_context": {
        const res = await callUnity("/assistant-context");
        return { content: [{ type: "text", text: JSON.stringify(res, null, 2) }] };
      }
      case "unity_get_status": {
        const res = await callUnity("/status");
        return { content: [{ type: "text", text: JSON.stringify(res, null, 2) }] };
      }
      case "unity_get_scene_summary": {
        const res = await callUnity("/scene");
        return { content: [{ type: "text", text: JSON.stringify(res, null, 2) }] };
      }
      case "unity_get_profiler_frame": {
        const res = await callUnity("/profiler");
        return { content: [{ type: "text", text: JSON.stringify(res, null, 2) }] };
      }
      case "unity_get_project_settings": {
        const res = await callUnity("/project-settings");
        return { content: [{ type: "text", text: JSON.stringify(res, null, 2) }] };
      }
      case "unity_apply_asset_optimization": {
        const res = await callUnity("/optimize", "POST", {
          assetPath: args.assetPath,
          type: args.type,
        });
        return { content: [{ type: "text", text: JSON.stringify(res, null, 2) }] };
      }
      default:
        return {
          isError: true,
          content: [{ type: "text", text: `Unknown tool: ${name}` }],
        };
    }
  } catch (error) {
    return {
      isError: true,
      content: [{ type: "text", text: `Error calling Unity bridge: ${error.message}` }],
    };
  }
});

// Hàm gọi tới HTTP Server trong Unity Editor
async function callUnity(path, method = "GET", body = null) {
  if (!process.env.OPTIFUNITY_CONNECTION) throw new Error("Set OPTIFUNITY_CONNECTION to the selected Editor Library/OptifunityBridge-<pid>.json file.");
  const connection = JSON.parse(readFileSync(process.env.OPTIFUNITY_CONNECTION, "utf8"));
  if (!connection || ["url", "token", "projectPath", "instanceId"].some(key => typeof connection[key] !== "string" || !connection[key]))
    throw new Error("Required connection fields must be nonempty strings.");
  if (!/^http:\/\/localhost:\d+$/.test(connection.url) || !connection.token || !connection.projectPath || !connection.instanceId)
    throw new Error("Invalid local Editor connection descriptor.");
  const url = connection.url + path;
  const options = {
    method,
    signal: AbortSignal.timeout(5000),
    headers: {
      "Content-Type": "application/json",
      "X-Optifunity-Token": connection.token,
      "X-Optifunity-Project": encodeURIComponent(connection.projectPath),
      "X-Optifunity-Instance": connection.instanceId,
    },
  };

  if (body) {
    options.body = JSON.stringify(body);
  }

  try {
    const response = await fetch(url, options);
    if (!response.ok) {
      const errText = await response.text();
      throw new Error(`HTTP ${response.status}: ${errText}`);
    }
    const result = await response.json();
    if (result.error) throw new Error(result.error);
    return result;
  } catch (err) {
    throw new Error(`Không thể kết nối tới Unity Editor Bridge tại ${url}. Hãy chắc chắn rằng Unity đang mở, đã biên dịch UnityMcpBridge.cs và cổng đã chọn không bị chặn. Chi tiết: ${err.message}`);
  }
}

// Chạy server qua luồng Stdio
const transport = new StdioServerTransport();
await server.connect(transport);
console.error("Unity MCP Server running on stdio transport");
