# Yin.Web Windows Server 部署

## 发布

在仓库根目录运行：

```powershell
dotnet publish Yin.Web\Yin.Web.csproj -c Release -o Yin.Web\publish
```

`Yin.Web\publish` 是完整自包含发布目录，可直接复制到 Windows Server。服务器不需要安装完全匹配的 .NET 8 补丁版本。

## 启动

```powershell
cd C:\Apps\Yin.Web
$env:YinWeb__AccessPassword = "change-this-password"
.\Yin.Web.exe --urls http://0.0.0.0:5088
```

浏览器访问 `http://服务器IP:5088`。安卓、iPhone、iPad 都直接用浏览器上传图片并下载结果。

## 生产建议

- 在云服务器安全组和 Windows 防火墙放行 `5088`，或改成你实际使用的端口。
- 公网使用时必须设置 `YinWeb__AccessPassword`，前端“访问密码”输入同一密码。
- 推荐用 IIS 反向代理或 Nginx/Caddy 做 HTTPS，再转发到 `127.0.0.1:5088`。
- 推荐用 Windows Service/NSSM 托管 `Yin.Web.exe`，保证开机自启。
