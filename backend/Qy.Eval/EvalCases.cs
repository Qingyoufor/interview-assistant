namespace Qy.Eval;

/// <summary>10 个合成 case：junior / mid / senior × 知识 / 项目，含两个带隐私标记的样本。</summary>
public static class EvalCases
{
    public static readonly IReadOnlyList<EvalCase> All =
    [
        new(
            "j1-knowledge", "junior", "knowledge",
            """
            李强
            2 年 Java 开发，专升本
            技能：Java、Spring Boot、MySQL、Git
            工作：外包公司做后台管理系统，负责增删改查接口
            """,
            """
            初级 Java 工程师
            职责：参与后台管理系统开发
            要求：Java 基础扎实；熟悉 Spring Boot；会写 SQL；有团队协作经验；有 Kafka 使用经验
            """,
            [
                "我用 Spring Boot 写过用户管理模块，主要是增删改查，数据库用 MySQL，接口按 REST 风格设计。",
                "索引我用过主键索引，没专门做过慢查询优化，遇到慢的时候会加 where 条件。"
            ],
            []),

        new(
            "j2-project", "junior", "project",
            """
            王芳
            2 年前端
            技能：Vue 3、TypeScript、Vite、Element Plus
            项目：课程表小程序，日活 300，前后端都是我写
            """,
            """
            前端工程师
            职责：负责管理后台页面开发与组件沉淀
            要求：Vue 3 项目经验；熟悉组件化；了解前端性能优化；了解 Node.js 服务端开发
            """,
            [
                "课程表小程序是我独立做的，用 Vue 3 加 TypeScript，页面按组件拆成列表、详情、编辑三块。",
                "性能上做过路由懒加载和图片压缩，没做过压测，所以没有具体数据。"
            ],
            []),

        new(
            "m1-knowledge", "mid", "knowledge",
            """
            陈晨，男，30 岁
            4 年后端开发，手机 13800138000，邮箱 chen.chen@example.com
            技能：Java、Spring Cloud、Kafka、Redis、MySQL
            工作：电商公司，负责订单服务，日均订单 5 万
            """,
            """
            中级 Java 工程师
            职责：负责交易链路开发与稳定性
            要求：3 年以上经验；熟悉 Kafka；熟悉 MySQL 索引优化；有高并发经验
            """,
            [
                "订单服务我用 Kafka 做异步落库，峰值 QPS 大概 200，消息积压时加过消费者。",
                "MySQL 加过联合索引，用 explain 看过执行计划，但没做过分库分表。"
            ],
            ["13800138000", "chen.chen@example.com", "陈晨"]),

        new(
            "m2-project", "mid", "project",
            """
            赵敏
            4 年全栈
            技能：Vue 3、.NET、MySQL、Redis
            项目：内部审批系统，20 个部门在用，我负责前后端
            """,
            """
            全栈工程师
            职责：独立负责业务模块从页面到接口
            要求：熟悉 Vue 与后端任一语言；了解缓存与事务；能写清楚技术方案；熟悉消息队列
            """,
            [
                "审批系统我从零做的，前端 Vue 3 加后端 .NET，流程配置用状态机存表，最麻烦的是驳回后重新走流程。",
                "缓存用 Redis 存权限，改权限的时候删 key，没有做双写一致性方案。"
            ],
            []),

        new(
            "m3-knowledge", "mid", "knowledge",
            """
            孙悦
            3 年 Python 开发
            技能：Python、FastAPI、LangChain、PostgreSQL
            工作：做过知识库问答，接的是 OpenAI 接口
            """,
            """
            AI 应用工程师
            职责：负责大模型应用落地与效果评估
            要求：熟悉 Python；了解 prompt 设计与结构化输出；有评测经验优先
            """,
            [
                "知识库问答我先做召回再做回答，prompt 里要求模型只根据检索到的片段回答，没命中就说不知道。",
                "评测我做得比较粗，主要看人工抽查，没有系统的指标。"
            ],
            []),

        new(
            "s1-knowledge", "senior", "knowledge",
            """
            周涛
            8 年后端，架构师
            技能：Java、Spring Cloud、Kafka、MySQL、Redis、Kubernetes
            经历：带 6 人团队做交易中台，QPS 峰值 3000
            """,
            """
            高级后端工程师/架构师
            职责：负责核心交易系统架构演进
            要求：5 年以上经验；精通分布式事务；有高并发系统设计经验；能带人
            """,
            [
                "交易中台我们按领域拆成下单、库存、支付三个服务，一致性用本地消息表加定时补偿，没有上 Seata。",
                "峰值 3000 QPS 主要是靠读写分离和缓存扛的，数据库分库分表做过一次，按用户 id 分 8 库。"
            ],
            []),

        new(
            "s2-project", "senior", "project",
            """
            吴磊，男，35 岁
            9 年后端，手机 13900139000，邮箱 li.lei@example.com
            技能：Go、Kafka、TiDB、Kubernetes
            项目：日志平台，日处理 20 亿条
            """,
            """
            资深后端工程师
            职责：负责数据平台架构与稳定性
            要求：熟悉 Kafka；有海量数据处理经验；熟悉容器化部署
            """,
            [
                "日志平台用 Kafka 做缓冲，下游 Flink 消费写 TiDB，日处理 20 亿条，扩容靠加 partition。",
                "Kubernetes 上做过资源限制和 HPA，踩过 pod 频繁重启的坑，最后是内存 limit 给低了。"
            ],
            ["13900139000", "li.lei@example.com", "吴磊"]),

        new(
            "s3-project", "senior", "project",
            """
            郑雪
            7 年算法/工程
            技能：Python、PyTorch、LangChain、向量库、Kubernetes
            项目：企业知识库平台，服务 30 个业务方
            """,
            """
            AI 平台工程师
            职责：负责大模型平台化与成本优化
            要求：熟悉 RAG 全链路；有平台化经验；关注推理成本；有模型量化与蒸馏落地经验
            """,
            [
                "平台把切分、向量化、召回、生成拆成四个服务，业务方只配参数，评测用固定 200 条问答集回归。",
                "成本上做过缓存和模型分级，简单问题走小模型，复杂问题才走大模型，成本降了大概四成。"
            ],
            []),

        new(
            "m4-project", "mid", "project",
            """
            冯静
            5 年测试，2 年自动化
            技能：Python、Pytest、Selenium、Jenkins
            项目：测试平台，覆盖 300 个接口
            """,
            """
            后端开发工程师（可接受转岗）
            职责：负责服务端接口开发
            要求：熟悉 Python 或 Java；有工程化意识；愿意从测试转开发；熟悉 MySQL 索引与事务
            """,
            [
                "测试平台是我用 Python 写的，接口用例配置化，跑在 Jenkins 上，每天出报告。",
                "转开发我自学了 FastAPI，写过两个内部小服务，还没在生产上扛过流量。"
            ],
            []),

        new(
            "s4-knowledge", "senior", "knowledge",
            """
            何军
            10 年后端，技术负责人
            技能：Java、微服务、MySQL、Redis、团队管理
            经历：带 12 人团队，负责支付系统
            """,
            """
            技术负责人
            职责：负责团队与系统，兼顾交付与技术选型
            要求：8 年以上经验；有团队管理经验；能做技术决策与取舍；有跨团队预算管理经验
            """,
            [
                "支付系统从单体拆到微服务是我推的，先拆对账再拆收银台，拆之前把监控和日志补齐。",
                "团队上我按业务线分组，每组一个 owner，季度做一次技术债盘点，优先级由我拍。"
            ],
            [])
    ];
}
