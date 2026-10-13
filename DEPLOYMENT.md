# Deploy TaskTrack AS2 lên Render

Cấu hình dành cho source backend AS2, đối chiếu tài liệu Render ngày 13/10/2026. Backend đã chạy tại [Render AS2](https://qe190064-prn232-ass2-be.onrender.com), [Swagger](https://qe190064-prn232-ass2-be.onrender.com/swagger); frontend tại [Vercel AS2](https://qe190064-prn232-ass2-fe.vercel.app). Các giá trị `YOUR_*` bên dưới là placeholder khi tạo môi trường mới.

Database `qe190064_prn232_ass2` đã được tạo riêng trong PostgreSQL Free hiện có, cùng region Singapore; database AS1 giữ nguyên. Instance này hết hạn **29/10/2026 lúc 01:53 giờ Việt Nam**. Admin đã seed bằng CLI, Staff đã Register qua UI live. Credentials giữ trong hồ sơ nộp riêng, không lưu trong Git. Database hiện có không cần khởi tạo lại.

## 1. Chọn database và dịch vụ

- Hai repo AS2 riêng đã có. Nếu cập nhật dịch vụ Render AS1 hiện có, kiểm tra repo/branch và database trước khi chuyển sang AS2. Nếu tạo dịch vụ mới, chọn repo public `NguyenNgu2k5/QE190064_PRN232_Ass2_BE`, branch `main`.
- Dùng database AS2 riêng theo cấu hình đã chốt để giữ dữ liệu AS1. Tạo PostgreSQL trong cùng region với backend. Ghi lại Internal Database URL cho Render và External Database URL cho máy local; không đưa hai URL có password vào Git hoặc hồ sơ nộp.
- Các thay đổi local phải được commit/push lên repo AS2 trước khi Render có thể build chúng. Xem diff trước khi push; việc có repo online không có nghĩa source local mới nhất đã được đưa lên đó.

Render hỗ trợ build Dockerfile từ repo; backend .NET dùng Docker runtime. [Docker on Render](https://render.com/docs/docker).

## 2. Database đã có schema hoặc database trống mới

Bản live AS2 đã có đủ sáu bảng và dữ liệu. Bản bổ sung refresh token/profile/audit dùng `dotnet TaskTrack.API.dll --upgrade-db` trước khi deploy code mới. Lệnh chỉ thêm cột/index/FK, chạy trong transaction và lặp lại được; không khởi tạo lại hoặc seed lại dữ liệu. Ngày 13/10 đã nâng cấp database AS2 local và production, đối chiếu checksum xác nhận dữ liệu cũ AS2 và AS1 giữ nguyên. Hai script SQL trước đây đã bỏ khỏi repo để giữ backend gọn như AS1.

Nếu tạo một database AS2 mới, tạo database trống bằng công cụ PostgreSQL/Render trước, đặt tên kết thúc bằng `_ass2`, rồi cấu hình riêng:

```powershell
Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
$productionDatabaseUrl = 'postgresql://YOUR_USER:YOUR_PASSWORD@YOUR_EXTERNAL_HOST:5432/YOUR_AS2_DB_ass2?sslmode=require'
$env:DATABASE_URL = $productionDatabaseUrl
$env:JWT_SECRET = 'YOUR_RANDOM_SECRET_WITH_AT_LEAST_32_UTF8_BYTES'
$env:ASPNETCORE_ENVIRONMENT = 'Production'
dotnet run --project TaskTrack.API --configuration Release --no-launch-profile -- --init-db
if ($LASTEXITCODE -ne 0) { throw 'Database initialization failed; stop here.' }
```

`--init-db` tạo schema từ model EF Core C# trong một transaction, không chèn dữ liệu mẫu AS1. Nó từ chối database có bảng và tên không kết thúc bằng `_ass2`; API startup thông thường không thay đổi schema. Lệnh này không nâng cấp một database AS1 cũ. Schema AS2 live đã được cập nhật trong lượt triển khai trước; khi cần dữ liệu hiện có ở môi trường mới, restore backup riêng thay vì khởi tạo lại.

Đề yêu cầu SystemAccount và seed Admin trực tiếp bằng migration/SQL; không yêu cầu hai file SQL riêng trong repository. Backend dùng EF Core có sẵn để tạo bảng, sau đó dùng CLI seed bên dưới để ghi Admin trực tiếp vào database. Không có endpoint setup hoặc seed public.

## 3. Seed Admin trực tiếp database

Không thêm endpoint seed. CLI của ứng dụng ghi PBKDF2 hash trực tiếp vào PostgreSQL và kết thúc sau khi seed. Dùng External URL nếu chạy CLI ở máy local. Những biến dưới đây chỉ dành cho terminal seed; không copy credentials local vào production.

```powershell
# Bỏ connection string cũ để DATABASE_URL trỏ đúng database production.
Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:DATABASE_URL = $productionDatabaseUrl
$secretBytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($secretBytes) } finally { $rng.Dispose() }
$env:JWT_SECRET = [Convert]::ToBase64String($secretBytes)
$env:ADMIN_EMAIL = 'YOUR_GRADING_ADMIN_EMAIL'
$env:ADMIN_PASSWORD = 'YOUR_PRIVATE_GRADING_ADMIN_PASSWORD'

dotnet run --project TaskTrack.API --configuration Release --no-launch-profile -- --seed-admin
if ($LASTEXITCODE -ne 0) { throw 'Admin seed failed.' }

# Xóa cấu hình production khỏi terminal; không chạy API local bằng cấu hình này.
Remove-Item Env:DATABASE_URL,Env:JWT_SECRET,Env:ADMIN_EMAIL,Env:ADMIN_PASSWORD,Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
Remove-Variable productionDatabaseUrl,secretBytes,rng -ErrorAction SilentlyContinue
```

JWT_SECRET ở bước seed chỉ để khởi tạo ứng dụng, không phát token; Render có signing secret riêng được tạo ở bước 4. Seed lặp cùng email Admin giữ nguyên tài khoản và password hiện có; không tự nâng quyền một email đang là Staff. Ghi credentials thật vào hồ sơ nộp riêng sau khi login online thành công.

Nếu Docker đang hoạt động, có thể chạy cùng CLI trong image bằng `dotnet TaskTrack.API.dll --seed-admin` với biến môi trường riêng. Không đặt `--seed-admin` làm startup command của web service vì process sẽ thoát. Gói Free không có Shell/one-off job, nên cách chạy CLI local ở trên phù hợp khi dùng gói này. [Giới hạn Render Free](https://render.com/docs/free).

## 4. Cấu hình Web Service

| Setting | Giá trị |
|---|---|
| Repository | `NguyenNgu2k5/QE190064_PRN232_Ass2_BE` |
| Branch | `main` |
| Language / Runtime | Docker |
| Root Directory | Để trống, repo backend đã là build context |
| Dockerfile Path | `./Dockerfile` |
| Docker Command | Để trống, dùng ENTRYPOINT hiện có |
| Port | Ứng dụng listen `0.0.0.0:8080` qua ASPNETCORE_URLS trong image |
| Health Check Path | `/api/tags` — kiểm tra API và database cùng lúc |

Đặt environment tại Render, không ghi vào Dockerfile:

| Biến | Giá trị |
|---|---|
| `DATABASE_URL` | Internal Database URL của database AS2 cùng region |
| `JWT_SECRET` | Secret production ngẫu nhiên, ít nhất 32 UTF-8 bytes |
| `CORS_ORIGINS` | Origin Vercel HTTPS thực tế, ví dụ `https://YOUR_PROJECT.vercel.app`, không `/` cuối |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

Tạo signing secret trong PowerShell bằng `RandomNumberGenerator` như bước 3 và nhập riêng vào Render. Không cần giữ ADMIN_EMAIL/ADMIN_PASSWORD trên web service sau khi seed. Không đặt đồng thời connection string cũ và DATABASE_URL mới vì connection string được ưu tiên.

Bấm deploy. Kiểm tra logs build/startup, `/api/tags` trả 200 và `/swagger` mở được. Swagger được giữ ở production để đáp ứng đề. Lấy URL HTTPS thực tế; không điền localhost vào hồ sơ nộp.

## 5. Nối frontend và kiểm tra online

1. Theo `DEPLOYMENT.md` ở repo frontend, đặt NEXT_PUBLIC_API_URL bằng backend HTTPS kèm `/api` và deploy Vercel.
2. Cập nhật CORS_ORIGINS bằng domain Vercel ổn định. Nếu dùng preview, thêm đúng origin preview cần kiểm tra; không dùng wildcard để thay quyền truy cập.
3. Dùng frontend live Register một Staff mới. Không tạo Staff bằng SQL seed.
4. Login Staff/Admin online, kiểm tra quyền và tất cả thao tác ở checklist bàn giao. Endpoint public mở được bằng trình duyệt; endpoint có quyền test trong Swagger với Bearer token.
5. Điền backend URL, frontend URL và credentials đã kiểm tra vào `QE190064_SE19B_Ass2.docx`.

Gói Free có thể ngủ sau 15 phút và database Free hết hạn sau 30 ngày. Kiểm tra ngày hết hạn trước lúc chấm; chờ dịch vụ thức dậy trước khi kết luận request thất bại. [Giới hạn Render Free](https://render.com/docs/free).

## 6. Kiểm tra image trên máy có Docker

```powershell
docker build --tag tasktrack-ass2:local .
if ($LASTEXITCODE -ne 0) { throw 'Docker build failed.' }
```

Image không cần secrets khi build. Khi chạy container, truyền biến bằng môi trường riêng; database local trong container không dùng `localhost` của Windows, dùng `host.docker.internal`. Không chạy integration suite hiện có với production: suite có thao tác tạo/xóa fixture và failure injection, đồng thời chủ động từ chối endpoint remote.
