#!/usr/bin/env node
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";

const UNITY_PORT = 9090;
const UNITY_URL = `http://localhost:${UNITY_PORT}`;

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
    description: "Tự động thiết lập cấu hình tối ưu hóa tài nguyên (nén ASTC 8x8, tắt Read/Write) cho Mesh hoặc Texture.",
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
  const url = `${UNITY_URL}${path}`;
  const options = {
    method,
    headers: {
      "Content-Type": "application/json",
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
    return await response.json();
  } catch (err) {
    throw new Error(`Không thể kết nối tới Unity Editor Bridge tại ${url}. Hãy chắc chắn rằng Unity đang mở, đã biên dịch UnityMcpBridge.cs và cổng ${UNITY_PORT} không bị chặn. Chi tiết: ${err.message}`);
  }
}

// Chạy server qua luồng Stdio
const transport = new StdioServerTransport();
await server.connect(transport);
console.error("Unity MCP Server running on stdio transport");
