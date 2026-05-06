Dưới đây là toàn bộ nội dung được trích xuất từ bức ảnh bạn cung cấp. Bức ảnh này thực sự là một bản tóm tắt kiến trúc cực kỳ xuất sắc và chính xác, đặc biệt là ở chi tiết "các đường tối ưu hóa có thể hoạt động song song" thay vì chỉ là một danh sách ưu tiên tuyến tính.

# ---

**PIPELINE RENDERING & BATCHING TRONG URP (Unity 6.x)**

Unity không sử dụng một luồng IF-ELSE tuyến tính.

Mỗi Renderer sẽ được đánh giá độc lập và có thể đi theo các đường tối ưu hóa khác nhau song song.

### **CÁC HỆ THỐNG CHÍNH**

* **GRD:** GPU Resident Drawer (GPU-driven rendering)  
* **SRP:** SRP Batcher (CPU optimization)  
* **GI:** GPU Instancing (Traditional)  
* **SB:** Static Batching (Build time)  
* **DB:** Dynamic Batching (Runtime, legacy)  
* **FD:** Fallback (Normal Draw) (Mỗi renderer \= 1 draw call)

### **QUY TRÌNH TỔNG QUAN (MỖI RENDERER ĐƯỢC XỬ LÝ ĐỘC LẬP)**

1. **Renderer:** (Mesh Renderer)  
2. **Culling (CPU / GPU):** Giảm draw calls bằng instancing. Frustum / Occlusion / LOD / Light layers...  
3. **Render Loop (URP):** Unity phân loại và gửi renderer tới các hệ thống phù hợp (nếu điều kiện đạt)  
4. **GPU:** Thực thi các draw calls được tạo bởi từng hệ thống (song song và độc lập)

### ---

**CÁC ĐƯỜNG TỐI ƯU HÓA (CÓ THỂ HOẠT ĐỘNG SONG SONG)**

**1\. GPU RESIDENT DRAWER (GRD) \- GPU-Driven Rendering**

* Dùng BatchRendererGroup  
* Instance data & culling trên GPU  
* Hỗ trợ LOD, occlusion culling trên GPU  
* Draw bằng DrawMeshInstancedIndirect  
* Hiệu quả nhất khi có nhiều instance giống nhau  
* *Luồng xử lý:* Instance Data Buffer (GPU) ➔ DrawMeshInstancedIndirect (BatchRendererGroup) ➔ Rất ít draw calls (thường 1-2)

**2\. SRP BATCHER \- CPU Optimization (Không giảm draw call)**

* Giảm chi phí SetPass / state changes  
* Giữ material data trên GPU  
* 1 renderer \= 1 draw call (hoặc nhiều hơn nếu nhiều pass)  
* Không tương thích với GPU Instancing  
* *Luồng xử lý:* Draw Call 1, Draw Call 3... ➔ SRP Batcher (Giảm CPU cost) ➔ Nhiều draw calls (CPU nhẹ hơn)

**3\. GPU INSTANCING (TRADITIONAL) \- Giảm draw calls bằng instancing**

* Các renderer có cùng Mesh \+ Material  
* Instance data gửi qua GPU  
* Draw bằng DrawMeshInstanced  
* Hỗ trợ per-instance data qua MPB (một số trường hợp)  
* Không tương thích với SRP Batcher trong nhiều trường hợp  
* *Luồng xử lý:* Instance 1, Instance 2, Instance N ➔ DrawMeshInstanced (GPU) ➔ Giảm số lượng draw calls (tối đa \~1023 instance/batch)

**4\. STATIC BATCHING \- Build Time**

* Gộp mesh ở build time  
* Cùng material ➔ 1 draw call  
* Tăng memory (mesh được copy)  
* Không phù hợp với object thay đổi  
* *Luồng xử lý:* Combined Mesh (Per Material) ➔ Draw Call (1 draw call / material) ➔ Giảm draw calls nhưng tăng memory

**5\. DYNAMIC BATCHING (LEGACY) \- Runtime (CPU)**

* Gộp các mesh nhỏ khi render  
* Giới hạn \~300 verts/mesh  
* Chạy trên CPU, tốn CPU  
* Ít khi mang lại lợi ích rõ rệt  
* *Luồng xử lý:* CPU Combine (Runtime) ➔ Draw Call ➔ Giảm draw calls ít, tốn CPU (không khuyến khích)

**6\. FALLBACK (NORMAL DRAW) \- Không tối ưu**

* Không thỏa điều kiện của các hệ thống trên  
* Mỗi renderer \= 1 draw call  
* *Luồng xử lý:* Draw Call 1, Draw Call 2, Draw Call N ➔ Nhiều draw calls (CPU \+ GPU tốn kém)

### ---

**SO SÁNH NHANH**

| HỆ THỐNG | GRD | SRP BATCHER | GPU INSTANCING | STATIC BATCHING | DYNAMIC BATCHING | FALLBACK |
| :---- | :---- | :---- | :---- | :---- | :---- | :---- |
| **Mục tiêu chính** | Giảm draw call (GPU-driven) | Giảm CPU (SetPass) | Giảm draw call | Giảm draw call | Giảm draw call ít | Không tối ưu |
| **Giảm draw call** | Rất nhiều (thường 1-2) | Không | Có (theo batch size) | Có (1/material) | Ít | Không |
| **Tối ưu CPU** | Rất tốt | Rất tốt | Trung bình | Tốt (runtime) | Kém (tốn CPU) | Kém |
| **Tối ưu GPU** | Rất tốt (culling trên GPU) | Trung bình | Tốt | Tốt | Trung bình | Kém |
| **Thời điểm** | Runtime | Runtime | Runtime | Build time | Runtime | Runtime |
| **Tăng memory** | Thấp | Thấp | Thấp | Cao (mesh copy) | Thấp | Thấp |
| **Điều kiện chính** | Support GRD \+ shader \+ không dùng MPB... | SRP shader compatible | Cùng Mesh \+ Material \+ shader hỗ trợ | Static \+ cùng Material | Mesh nhỏ (\~300 verts) | Không thỏa hệ thống khác |
| **Tương thích với hệ thống khác** | Không dùng Static Batching (khuyến khích) | Không tương thích với GPU Instancing | Không tương thích với SRP Batcher (thường) | Không dùng với GRD (khuyến khích) | Không tương thích với SRP Batcher / GI / GRD | \- |
| **Khuyến nghị (2024+)** | Cho scenes lớn, nhiều instance lặp lại | Luôn bật (mặc định URP) | Dùng khi không dùng GRD và có instance vừa phải | Dùng cho static thật sự ít thay đổi | Hạn chế dùng | Tránh |

### ---

**KHI NÀO NÊN DÙNG HỆ THỐNG NÀO?**

* Scene rất lớn, nhiều object lặp lại (cây, cỏ, đá, props...) ➔ **Dùng GRD**  
* Scene nhiều material, nhiều object nhưng không thể instance ➔ **Dùng SRP Batcher**  
* Có các nhóm object vừa phải, muốn giảm draw call ➔ **Dùng GPU Instancing**  
* Environment static (tường, nhà, đường...) ➔ **Dùng Static Batching (cẩn thận memory)**  
* Object rất nhỏ, không thể instance ➔ **Chỉ cân nhắc Dynamic Batching (legacy)**  
* Các trường hợp còn lại ➔ **Fallback**

### **QUAN TRỌNG: GRD VS CÁC HỆ THỐNG KHÁC**

* **\[✓\]** GRD là pipeline riêng (BatchRendererGroup), ưu tiên khi đủ điều kiện.  
* **\[\!\]** Nên TẮT Static Batching khi sử dụng GRD để tránh xung đột.  
* **\[X\]** GRD không hoạt động nếu dùng MaterialPropertyBlock (MPB).  
* **\[\!\]** GPU Instancing và SRP Batcher thường không tương thích.  
* **\[i\] Frame Debugger:** Nếu thấy "DrawMeshInstancedIndirect" ➔ là GRD. Nếu thấy "SRP Batcher" ➔ là SRP Batcher. Nếu thấy "Draw Call" ➔ fallback.

---

**LƯU Ý CHUNG:** Số lượng draw calls thực tế phụ thuộc vào nhiều yếu tố: số material, pass, lightmap, shader keyword, camera, culling, LOD, rendering layers, ...

**Version:** Unity 6.x (URP)